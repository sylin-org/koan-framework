namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Tracks lineage relationships and mutations for canonical entities.
/// </summary>
public sealed class CanonLineage
{
    private readonly object _gate = new();
    private HashSet<string> _parents = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _children = new(StringComparer.OrdinalIgnoreCase);
    private List<CanonLineageChange> _changes = new();

    /// <summary>
    /// Parent canonical identifiers.
    /// </summary>
    public HashSet<string> Parents
    {
        get => _parents;
        set => _parents = value is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Child canonical identifiers.
    /// </summary>
    public HashSet<string> Children
    {
        get => _children;
        set => _children = value is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recorded lineage changes.
    /// </summary>
    public List<CanonLineageChange> Changes
    {
        get => _changes;
        set => _changes = value ?? new List<CanonLineageChange>();
    }

    /// <summary>
    /// Adds a parent relationship.
    /// </summary>
    public bool AddParent(string canonicalId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        lock (_gate)
        {
            if (_parents.Add(canonicalId))
            {
                _changes.Add(new CanonLineageChange(CanonLineageChangeKind.ParentLinked, canonicalId, DateTimeOffset.UtcNow, notes));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes a parent relationship.
    /// </summary>
    public bool RemoveParent(string canonicalId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            return false;
        }

        lock (_gate)
        {
            if (_parents.Remove(canonicalId))
            {
                _changes.Add(new CanonLineageChange(CanonLineageChangeKind.ParentUnlinked, canonicalId, DateTimeOffset.UtcNow, notes));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a child relationship.
    /// </summary>
    public bool AddChild(string canonicalId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        lock (_gate)
        {
            if (_children.Add(canonicalId))
            {
                _changes.Add(new CanonLineageChange(CanonLineageChangeKind.ChildLinked, canonicalId, DateTimeOffset.UtcNow, notes));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes a child relationship.
    /// </summary>
    public bool RemoveChild(string canonicalId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            return false;
        }

        lock (_gate)
        {
            if (_children.Remove(canonicalId))
            {
                _changes.Add(new CanonLineageChange(CanonLineageChangeKind.ChildUnlinked, canonicalId, DateTimeOffset.UtcNow, notes));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records that the entity was superseded by the provided canonical identifier.
    /// </summary>
    public void MarkSupersededBy(string canonicalId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        lock (_gate)
        {
            _changes.Add(new CanonLineageChange(CanonLineageChangeKind.SupersededBy, canonicalId, DateTimeOffset.UtcNow, notes));
        }
    }

    /// <summary>
    /// Records that this entity superseded another canonical identifier.
    /// </summary>
    public void MarkSuperseded(string canonicalId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        lock (_gate)
        {
            _changes.Add(new CanonLineageChange(CanonLineageChangeKind.Superseded, canonicalId, DateTimeOffset.UtcNow, notes));
        }
    }

    /// <summary>
    /// Records a metadata update without structural change.
    /// </summary>
    public void RecordMetadataUpdate(string notes)
    {
        lock (_gate)
        {
            _changes.Add(new CanonLineageChange(CanonLineageChangeKind.MetadataUpdated, "", DateTimeOffset.UtcNow, notes));
        }
    }

    /// <summary>
    /// Creates a deep copy of the lineage data.
    /// </summary>
    public CanonLineage Clone()
    {
        lock (_gate)
        {
            return new CanonLineage
            {
                Parents = new HashSet<string>(_parents, StringComparer.OrdinalIgnoreCase),
                Children = new HashSet<string>(_children, StringComparer.OrdinalIgnoreCase),
                Changes = _changes.Select(change => new CanonLineageChange(change.Kind, change.RelatedId, change.OccurredAt, change.Notes)).ToList()
            };
        }
    }

    /// <summary>
    /// Merges lineage information from an incoming snapshot.
    /// </summary>
    public void Merge(CanonLineage incoming, bool preferIncoming)
    {
        if (incoming is null)
        {
            throw new ArgumentNullException(nameof(incoming));
        }

        lock (_gate)
        {
            foreach (var parent in incoming._parents)
            {
                _parents.Add(parent);
            }

            foreach (var child in incoming._children)
            {
                _children.Add(child);
            }

            if (preferIncoming && incoming._changes.Count > 0)
            {
                _changes = incoming._changes
                    .Select(change => new CanonLineageChange(change.Kind, change.RelatedId, change.OccurredAt, change.Notes))
                    .ToList();
            }
            else
            {
                foreach (var change in incoming._changes)
                {
                    _changes.Add(new CanonLineageChange(change.Kind, change.RelatedId, change.OccurredAt, change.Notes));
                }
            }
        }
    }
}
