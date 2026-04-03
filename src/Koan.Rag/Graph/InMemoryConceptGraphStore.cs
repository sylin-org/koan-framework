using System.Collections.Concurrent;
using System.Text.Json;
using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Graph;

/// <summary>
/// In-memory concept graph with periodic snapshot persistence to <c>.Koan/cache/rag/</c>.
/// Suitable for corpora up to ~500K entities (~55MB memory).
/// <para>
/// Implements single-writer (ingestion pipeline) + multi-reader (retrieval pipeline)
/// concurrency model. Reads are lock-free; writes use a lightweight lock.
/// </para>
/// </summary>
internal sealed class InMemoryConceptGraphStore : IConceptGraphStore
{
    private readonly ILogger<InMemoryConceptGraphStore> _logger;
    private readonly string _persistencePath;
    private readonly object _writeLock = new();

    // Core graph storage — concurrent reads, locked writes
    private readonly ConcurrentDictionary<string, ConceptEntity> _entities = new();
    private readonly ConcurrentDictionary<string, ConceptRelationship> _relationships = new();

    // Adjacency index for fast neighborhood queries
    private readonly ConcurrentDictionary<string, HashSet<string>> _adjacency = new();

    // Document provenance: entity ID → set of source document IDs
    private readonly ConcurrentDictionary<string, HashSet<string>> _entityDocuments = new();

    private DateTimeOffset? _lastPersisted;

    public InMemoryConceptGraphStore(ILogger<InMemoryConceptGraphStore> logger)
    {
        _logger = logger;

        // Use the .Koan/ persistent storage convention
        var basePath = Path.Combine(
            AppContext.BaseDirectory, ".Koan", "cache", "rag");
        Directory.CreateDirectory(basePath);
        _persistencePath = Path.Combine(basePath, "concept-graph.json");
    }

    public async Task Load(CancellationToken ct = default)
    {
        if (!File.Exists(_persistencePath))
        {
            _logger.LogDebug("No persisted concept graph found at {Path}", _persistencePath);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_persistencePath);
            var snapshot = await JsonSerializer.DeserializeAsync<GraphSnapshot>(stream, cancellationToken: ct);

            if (snapshot is null) return;

            lock (_writeLock)
            {
                _entities.Clear();
                _relationships.Clear();
                _adjacency.Clear();
                _entityDocuments.Clear();

                foreach (var entity in snapshot.Entities)
                    _entities[entity.Id] = entity;

                foreach (var rel in snapshot.Relationships)
                {
                    _relationships[rel.Id] = rel;
                    AddToAdjacency(rel.FromEntityId, rel.Id);
                    AddToAdjacency(rel.ToEntityId, rel.Id);
                }

                foreach (var (entityId, docIds) in snapshot.EntityDocuments)
                    _entityDocuments[entityId] = new HashSet<string>(docIds);
            }

            _lastPersisted = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "Loaded concept graph: {Entities} entities, {Relationships} relationships",
                _entities.Count, _relationships.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load concept graph from {Path}", _persistencePath);
        }
    }

    public async Task Save(CancellationToken ct = default)
    {
        try
        {
            Dictionary<string, IReadOnlyList<string>> entityDocumentsSnapshot;
            lock (_writeLock)
            {
                entityDocumentsSnapshot = _entityDocuments.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToList() as IReadOnlyList<string>);
            }

            var snapshot = new GraphSnapshot
            {
                Entities = _entities.Values.ToList(),
                Relationships = _relationships.Values.ToList(),
                EntityDocuments = entityDocumentsSnapshot
            };

            var tempPath = _persistencePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: ct);
            }

            File.Move(tempPath, _persistencePath, overwrite: true);
            _lastPersisted = DateTimeOffset.UtcNow;

            _logger.LogDebug(
                "Persisted concept graph: {Entities} entities, {Relationships} relationships",
                snapshot.Entities.Count, snapshot.Relationships.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist concept graph to {Path}", _persistencePath);
        }
    }

    public Task<GraphNeighborhood> GetNeighborhood(
        string entityId, int depth = 1, CancellationToken ct = default)
    {
        var visitedEntities = new HashSet<string>();
        var visitedRelationships = new HashSet<string>();
        var frontier = new HashSet<string> { entityId };

        for (var d = 0; d <= depth && frontier.Count > 0; d++)
        {
            var nextFrontier = new HashSet<string>();

            foreach (var eid in frontier)
            {
                if (!visitedEntities.Add(eid)) continue;

                IReadOnlyList<string> relIds;
                lock (_writeLock)
                {
                    relIds = _adjacency.TryGetValue(eid, out var set)
                        ? set.ToList()
                        : [];
                }

                if (relIds.Count > 0)
                {
                    foreach (var relId in relIds)
                    {
                        if (!visitedRelationships.Add(relId)) continue;

                        if (_relationships.TryGetValue(relId, out var rel))
                        {
                            nextFrontier.Add(rel.FromEntityId);
                            nextFrontier.Add(rel.ToEntityId);
                        }
                    }
                }
            }

            frontier = nextFrontier;
        }

        var entities = visitedEntities
            .Where(id => _entities.ContainsKey(id))
            .Select(id => _entities[id])
            .ToList();

        var relationships = visitedRelationships
            .Where(id => _relationships.ContainsKey(id))
            .Select(id => _relationships[id])
            .ToList();

        return Task.FromResult(new GraphNeighborhood(entities, relationships));
    }

    public Task ApplyDelta(GraphDelta delta, CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            // Add entities
            foreach (var entity in delta.AddedEntities)
                _entities[entity.Id] = entity;

            // Add relationships
            foreach (var rel in delta.AddedRelationships)
            {
                _relationships[rel.Id] = rel;
                AddToAdjacency(rel.FromEntityId, rel.Id);
                AddToAdjacency(rel.ToEntityId, rel.Id);
            }

            // Remove entities
            foreach (var entityId in delta.RemovedEntityIds)
            {
                _entities.TryRemove(entityId, out _);
                _adjacency.TryRemove(entityId, out _);
                _entityDocuments.TryRemove(entityId, out _);
            }

            // Remove relationships
            foreach (var relId in delta.RemovedRelationshipIds)
            {
                if (_relationships.TryRemove(relId, out var removed))
                {
                    RemoveFromAdjacency(removed.FromEntityId, relId);
                    RemoveFromAdjacency(removed.ToEntityId, relId);
                }
            }

            // Apply mention deltas (reference counting)
            foreach (var mention in delta.MentionDeltas)
            {
                if (_entities.TryGetValue(mention.EntityId, out var entity))
                {
                    var newCount = entity.MentionCount + mention.Delta;

                    if (newCount <= 0)
                    {
                        // Prune entity at zero mentions
                        _entities.TryRemove(mention.EntityId, out _);
                        _adjacency.TryRemove(mention.EntityId, out _);
                        _entityDocuments.TryRemove(mention.EntityId, out _);
                    }
                    else
                    {
                        _entities[mention.EntityId] = entity with { MentionCount = newCount };
                    }
                }

                // Track document provenance
                if (mention.Delta > 0)
                {
                    var docs = _entityDocuments.GetOrAdd(mention.EntityId, _ => []);
                    docs.Add(mention.DocumentId);
                }
                else if (_entityDocuments.TryGetValue(mention.EntityId, out var existingDocs))
                {
                    existingDocs.Remove(mention.DocumentId);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task Clear(CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            _entities.Clear();
            _relationships.Clear();
            _adjacency.Clear();
            _entityDocuments.Clear();
        }

        _logger.LogInformation("Concept graph cleared");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConceptEntity>> GetAllEntities(CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            return Task.FromResult<IReadOnlyList<ConceptEntity>>(_entities.Values.ToList());
        }
    }

    public GraphStats GetStats()
    {
        var entityCount = _entities.Count;
        var relCount = _relationships.Count;
        var density = entityCount > 1
            ? (double)relCount / (entityCount * (entityCount - 1))
            : 0.0;

        return new GraphStats(entityCount, relCount, density, _lastPersisted);
    }

    private void AddToAdjacency(string entityId, string relationshipId)
    {
        var set = _adjacency.GetOrAdd(entityId, _ => []);
        set.Add(relationshipId);
    }

    private void RemoveFromAdjacency(string entityId, string relationshipId)
    {
        if (_adjacency.TryGetValue(entityId, out var set))
            set.Remove(relationshipId);
    }

    /// <summary>
    /// Serializable snapshot for persistence.
    /// </summary>
    private sealed class GraphSnapshot
    {
        public List<ConceptEntity> Entities { get; init; } = [];
        public List<ConceptRelationship> Relationships { get; init; } = [];
        public Dictionary<string, IReadOnlyList<string>> EntityDocuments { get; init; } = new();
    }
}
