using System.Diagnostics;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Distillation;

/// <summary>
/// Implements the RAPTOR algorithm: recursive embed → cluster → summarize.
/// Builds a hierarchical distillation tree from leaf chunk embeddings,
/// producing multi-resolution summaries that enable collapsed tree retrieval.
/// <para>
/// Operates at two scopes:
/// <list type="bullet">
///   <item>Per-document: small tree capturing internal document structure (built during ingestion)</item>
///   <item>Corpus-wide: cross-document tree connecting thematic clusters (built on demand)</item>
/// </list>
/// </para>
/// </summary>
internal sealed class DistillationTreeBuilder
{
    private readonly IClusteringStrategy _clustering;
    private readonly IDistillationTreeStore _treeStore;
    private readonly ILogger<DistillationTreeBuilder> _logger;
    private readonly RagOptions _options;
    private readonly string? _summarizeModel;

    private long _currentVersion;

    public DistillationTreeBuilder(
        IClusteringStrategy clustering,
        IDistillationTreeStore treeStore,
        IOptions<RagOptions> options,
        ILogger<DistillationTreeBuilder> logger)
    {
        _clustering = clustering;
        _treeStore = treeStore;
        _logger = logger;
        _options = options.Value;
        _summarizeModel = options.Value.Models.Summarize;
    }

    /// <summary>
    /// Build a distillation tree from leaf chunk embeddings. Recursively
    /// clusters and summarizes until the adaptive depth limit or convergence.
    /// </summary>
    /// <param name="leafEmbeddings">Leaf chunk embeddings with their IDs.</param>
    /// <param name="directive">Corpus directive for summarization context.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task BuildTree(
        IReadOnlyList<EmbeddingWithId> leafEmbeddings,
        string? directive,
        CancellationToken ct)
    {
        if (leafEmbeddings.Count < 2)
        {
            _logger.LogDebug("Skipping tree build: only {Count} leaf chunks", leafEmbeddings.Count);
            return;
        }

        var sw = Stopwatch.StartNew();
        _currentVersion++;

        var clusterFactor = _options.TreeClusterFactor;
        var maxDepth = _options.TreeMaxDepth
            ?? Math.Min(5, (int)Math.Ceiling(
                Math.Log(leafEmbeddings.Count) / Math.Log(clusterFactor)));

        _logger.LogInformation(
            "Building distillation tree: {Leaves} leaves, target depth {Depth}, cluster factor {Factor}",
            leafEmbeddings.Count, maxDepth, clusterFactor);

        // Start with leaf embeddings as the current level's input
        var currentLevel = leafEmbeddings;
        var allNewNodes = new List<DistillationNode>();

        for (var level = 1; level <= maxDepth; level++)
        {
            ct.ThrowIfCancellationRequested();

            if (currentLevel.Count < 2)
                break; // Cannot cluster fewer than 2 items

            var targetClusters = Math.Max(1, currentLevel.Count / clusterFactor);

            _logger.LogDebug(
                "Level {Level}: clustering {Count} items into ~{Target} clusters",
                level, currentLevel.Count, targetClusters);

            // Cluster
            var clusters = await _clustering.Cluster(currentLevel, targetClusters, ct);

            if (clusters.Count == 0)
                break;

            // Summarize each cluster
            var levelNodes = new List<DistillationNode>();

            foreach (var cluster in clusters)
            {
                ct.ThrowIfCancellationRequested();

                if (cluster.MemberIds.Count == 0)
                    continue;

                // Build the text to summarize
                var memberTexts = await GatherMemberTexts(cluster.MemberIds, level, allNewNodes, ct);
                if (string.IsNullOrWhiteSpace(memberTexts))
                    continue;

                // Summarize via LLM
                var summary = await Summarize(memberTexts, directive, level, ct);
                if (string.IsNullOrWhiteSpace(summary))
                    continue;

                // Embed the summary
                var embedding = await EmbedSummary(summary, ct);

                var node = new DistillationNode
                {
                    Id = $"raptor:L{level}:c{cluster.ClusterId}:v{_currentVersion}",
                    Level = level,
                    Summary = summary,
                    Embedding = new ReadOnlyMemory<float>(embedding),
                    ChildNodeIds = cluster.MemberIds,
                    SourceDocumentIds = ExtractSourceDocuments(cluster.MemberIds),
                    CorpusVersion = _currentVersion
                };

                levelNodes.Add(node);
            }

            if (levelNodes.Count == 0)
                break;

            allNewNodes.AddRange(levelNodes);

            // Prepare input for next level: the summaries we just created
            currentLevel = levelNodes
                .Select(n => new EmbeddingWithId(n.Id, n.Embedding.ToArray()))
                .ToList();

            _logger.LogDebug(
                "Level {Level}: produced {Count} summary nodes",
                level, levelNodes.Count);
        }

        // Apply all new nodes to the tree store
        if (allNewNodes.Count > 0)
        {
            await _treeStore.ApplyDelta(new TreeDelta
            {
                AddedNodes = allNewNodes
            }, ct);

            await _treeStore.Save(ct);
        }

        sw.Stop();
        _logger.LogInformation(
            "Distillation tree built: {Nodes} nodes across {Levels} levels in {Duration}",
            allNewNodes.Count,
            allNewNodes.Count > 0 ? allNewNodes.Max(n => n.Level) : 0,
            sw.Elapsed);
    }

    // ── Member Text Gathering ───────────────────────────────────────────

    private async Task<string> GatherMemberTexts(
        IReadOnlyList<string> memberIds,
        int level,
        IReadOnlyList<DistillationNode> previousLevelNodes,
        CancellationToken ct)
    {
        var texts = new List<string>();

        foreach (var memberId in memberIds.Take(15)) // Cap to prevent prompt overflow
        {
            // Check if this ID refers to a tree node we already built
            var existingNode = previousLevelNodes.FirstOrDefault(n => n.Id == memberId);
            if (existingNode is not null)
            {
                texts.Add(existingNode.Summary);
                continue;
            }

            // Otherwise it's a leaf chunk or a previously stored tree node
            var stored = await _treeStore.GetNode(memberId, ct);
            if (stored is not null)
            {
                texts.Add(stored.Summary);
                continue;
            }

            // Leaf chunk: ID is like "{documentId}:{childId}" — we'd need to load
            // the chunk text from the vector store metadata. For now, use the ID
            // as a placeholder. The real implementation would hydrate from chunk store.
            texts.Add($"[Chunk: {memberId}]");
        }

        return string.Join("\n\n---\n\n", texts);
    }

    // ── Summarization ───────────────────────────────────────────────────

    private async Task<string> Summarize(
        string memberTexts,
        string? directive,
        int level,
        CancellationToken ct)
    {
        var directiveLine = directive is not null
            ? $"Domain guidance: {directive}\n\n"
            : "";

        var levelContext = level switch
        {
            1 => "These are raw text chunks from documents.",
            2 => "These are summaries of document sections.",
            _ => "These are high-level thematic summaries."
        };

        var prompt = $"{directiveLine}" +
                     $"Summarize the following content into a single coherent passage " +
                     $"that captures the key information, themes, and relationships. " +
                     $"{levelContext} Preserve specific details, names, and data points. " +
                     $"The summary should be self-contained and understandable without the source material.\n\n" +
                     $"Content:\n{memberTexts}";

        try
        {
            using (_summarizeModel is not null
                ? Koan.AI.Client.Scope(chat: _summarizeModel)
                : null)
            {
                return await Koan.AI.Client.Chat(prompt, ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Summarization failed at level {Level}", level);
            return "";
        }
    }

    private static async Task<float[]> EmbedSummary(string summary, CancellationToken ct)
    {
        return await Koan.AI.Client.Embed(summary, ct);
    }

    private static IReadOnlyList<string> ExtractSourceDocuments(IReadOnlyList<string> memberIds)
    {
        // Extract document IDs from chunk IDs (format: "{docId}:{chunkId}")
        return memberIds
            .Select(id =>
            {
                var colonIndex = id.IndexOf(':');
                return colonIndex > 0 ? id[..colonIndex] : id;
            })
            .Distinct()
            .ToList();
    }
}
