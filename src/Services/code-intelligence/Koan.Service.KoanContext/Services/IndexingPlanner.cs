using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Koan.Context.Models;
using Koan.Context.Utilities;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Computes differential indexing plans by comparing the persisted manifest with the live project tree.
/// </summary>
public sealed class IndexingPlanner
{
    private readonly Discovery _discovery;
    private readonly ILogger<IndexingPlanner> _logger;

    public IndexingPlanner(Discovery discovery, ILogger<IndexingPlanner> logger)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds a differential plan highlighting additions, changes, deletions, orphaned chunks, and missing metadata.
    /// </summary>
    /// <param name="projectId">Project identifier (partition id).</param>
    /// <param name="projectRootPath">Absolute project root path.</param>
    /// <param name="forceReindex">When true, treat every discovered file as changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IndexingPlan> PlanAsync(
        string projectId,
        string projectRootPath,
        bool forceReindex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);

        var stopwatch = Stopwatch.StartNew();
        var plan = new IndexingPlan();

        _logger.LogInformation("Planning differential scan for project {ProjectId}", projectId);

        Dictionary<string, IndexedFile> manifest;
        Dictionary<string, int> chunkCounts = new(StringComparer.OrdinalIgnoreCase);

        // Load manifest and current chunk state inside partition scope
        using (EntityContext.Partition(projectId))
        {
            var existingFiles = await IndexedFile.All(cancellationToken);
            manifest = existingFiles.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

            await foreach (var chunk in Chunk.AllStream(ct: cancellationToken))
            {
                chunkCounts.TryGetValue(chunk.FilePath, out var current);
                chunkCounts[chunk.FilePath] = current + 1;

                if (!manifest.ContainsKey(chunk.FilePath))
                {
                    plan.OrphanedChunkFiles.Add(chunk.FilePath);
                }
            }
        }

        _logger.LogDebug("Loaded manifest with {Count} files", manifest.Count);

        var discoveredFiles = await _discovery
            .DiscoverAsync(projectRootPath, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in discoveredFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            discoveredPaths.Add(file.RelativePath);

            if (forceReindex)
            {
                plan.ChangedFiles.Add(file);
                continue;
            }

            if (!manifest.TryGetValue(file.RelativePath, out var existing))
            {
                plan.NewFiles.Add(file);
                _logger.LogTrace("New file: {Path}", file.RelativePath);
                continue;
            }

            var currentHash = await FileHasher.ComputeSha256Async(file.AbsolutePath, cancellationToken);

            if (string.Equals(currentHash, existing.ContentHash, StringComparison.Ordinal))
            {
                var hasChunks = chunkCounts.TryGetValue(file.RelativePath, out var count) && count > 0;
                if (hasChunks)
                {
                    plan.SkippedFiles.Add(file);
                    _logger.LogTrace("Skipped (unchanged): {Path}", file.RelativePath);
                }
                else
                {
                    plan.ChangedFiles.Add(file);
                    plan.FilesMissingChunks.Add(file.RelativePath);
                    _logger.LogTrace("Missing chunk metadata: {Path}", file.RelativePath);
                }
            }
            else
            {
                plan.ChangedFiles.Add(file);
                _logger.LogTrace("Changed file: {Path}", file.RelativePath);
            }
        }

        foreach (var manifestPath in manifest.Keys)
        {
            if (!discoveredPaths.Contains(manifestPath))
            {
                plan.DeletedFiles.Add(manifestPath);
                _logger.LogTrace("Deleted file: {Path}", manifestPath);
            }
            else if (!chunkCounts.ContainsKey(manifestPath))
            {
                plan.FilesMissingChunks.Add(manifestPath);
            }
        }

        stopwatch.Stop();
        plan.PlanningTime = stopwatch.Elapsed;

        var avgTimePerFile = TimeSpan.FromMilliseconds(500);
        plan.EstimatedTimeSavings = avgTimePerFile * plan.SkippedFiles.Count;

        _logger.LogInformation(
            "Planning complete: {Plan} (saved ~{Savings:F1}s)",
            plan,
            plan.EstimatedTimeSavings.TotalSeconds);

        return plan;
    }
}
