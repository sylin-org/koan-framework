using System.Collections.Concurrent;
using System.Text.Json;
using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Distillation;

/// <summary>
/// In-memory distillation tree with JSON snapshot persistence to
/// <c>.Koan/cache/rag/{partition}/distillation-tree.json</c>.
/// Partition-aware: reads the active partition from <c>EntityContext</c> at construction time.
/// <para>
/// Partition is captured at DI construction time. For multi-partition operation,
/// register stores as scoped or use a partition-keyed factory.
/// </para>
/// </summary>
internal sealed class InMemoryDistillationTreeStore : IDistillationTreeStore
{
    private readonly ILogger<InMemoryDistillationTreeStore> _logger;
    private readonly ConcurrentDictionary<string, DistillationNode> _nodes = new();
    private readonly object _writeLock = new();
    private readonly string _persistencePath;
    private long _currentVersion;
    private DateTimeOffset? _lastBuildTime;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public InMemoryDistillationTreeStore(ILogger<InMemoryDistillationTreeStore> logger)
    {
        _logger = logger;
        var partition = Koan.Data.Core.EntityContext.Current?.Partition ?? "default";
        var basePath = Path.Combine(
            AppContext.BaseDirectory, ".Koan", "cache", "rag", partition);
        _persistencePath = Path.Combine(basePath, "distillation-tree.json");
    }

    public async Task Load(CancellationToken ct = default)
    {
        var path = _persistencePath;
        if (!File.Exists(path))
        {
            _logger.LogDebug("No persisted distillation tree at {Path}", path);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<TreeSnapshot>(
                stream, JsonOptions, ct);

            if (snapshot is null) return;

            if (snapshot.SchemaVersion != 1)
            {
                _logger.LogWarning(
                    "Distillation tree snapshot has unsupported schema version {Version}, skipping",
                    snapshot.SchemaVersion);
                return;
            }

            lock (_writeLock)
            {
                _nodes.Clear();
                foreach (var node in snapshot.Nodes)
                    _nodes[node.Id] = node;
                _currentVersion = snapshot.Version;
                _lastBuildTime = snapshot.LastBuildTime;
            }

            _logger.LogInformation(
                "Loaded distillation tree: {Nodes} nodes, version {Version}",
                _nodes.Count, _currentVersion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load distillation tree from {Path}", path);
        }
    }

    public async Task Save(CancellationToken ct = default)
    {
        var path = _persistencePath;

        try
        {
            List<DistillationNode> nodeSnapshot;
            long version;
            DateTimeOffset? buildTime;

            lock (_writeLock)
            {
                nodeSnapshot = _nodes.Values.ToList();
                version = _currentVersion;
                buildTime = _lastBuildTime;
            }

            var snapshot = new TreeSnapshot
            {
                Nodes = nodeSnapshot,
                Version = version,
                LastBuildTime = buildTime
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var tempPath = path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct);
            }

            File.Move(tempPath, path, overwrite: true);

            _logger.LogDebug(
                "Persisted distillation tree: {Nodes} nodes, version {Version}",
                nodeSnapshot.Count, version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist distillation tree to {Path}", path);
        }
    }

    public Task<DistillationNode?> GetNode(string nodeId, CancellationToken ct = default)
    {
        _nodes.TryGetValue(nodeId, out var node);
        return Task.FromResult(node);
    }

    public Task<IReadOnlyList<DistillationNode>> GetLevel(int level, CancellationToken ct = default)
    {
        IReadOnlyList<DistillationNode> result;
        lock (_writeLock)
        {
            result = _nodes.Values.Where(n => n.Level == level).ToList();
        }
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DistillationNode>> GetAllNodes(CancellationToken ct = default)
    {
        IReadOnlyList<DistillationNode> result;
        lock (_writeLock)
        {
            result = _nodes.Values.ToList();
        }
        return Task.FromResult(result);
    }

    public Task ApplyDelta(TreeDelta delta, CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            foreach (var nodeId in delta.RemovedNodeIds)
                _nodes.TryRemove(nodeId, out _);

            foreach (var node in delta.AddedNodes)
                _nodes[node.Id] = node;

            if (delta.AddedNodes.Count > 0)
                _lastBuildTime = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task Clear(CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            _nodes.Clear();
        }

        _logger.LogInformation("Distillation tree cleared");
        return Task.CompletedTask;
    }

    public DistillationTreeStats GetStats()
    {
        int totalNodes;
        int maxLevel;

        lock (_writeLock)
        {
            totalNodes = _nodes.Count;
            maxLevel = totalNodes > 0 ? _nodes.Values.Max(n => n.Level) : 0;
        }

        return new DistillationTreeStats(totalNodes, maxLevel, _currentVersion, _lastBuildTime);
    }

    private sealed class TreeSnapshot
    {
        public int SchemaVersion { get; init; } = 1;
        public List<DistillationNode> Nodes { get; init; } = [];
        public long Version { get; init; }
        public DateTimeOffset? LastBuildTime { get; init; }
    }
}
