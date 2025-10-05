using System.Diagnostics.CodeAnalysis;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Metadata snapshot persisted alongside canonical entities and value objects.
/// </summary>
public sealed class CanonMetadata
{
    private readonly object _gate = new();
    private Dictionary<string, CanonExternalId> _externalIds = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CanonSourceAttribution> _sources = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CanonPolicySnapshot> _policies = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private CanonLineage _lineage = new();
    private CanonState _state = CanonState.Default;

    /// <summary>
    /// Gets or sets the canonical identifier linked to the metadata.
    /// </summary>
    public string? CanonicalId { get; private set; }

    /// <summary>
    /// Indicates whether the metadata has an assigned canonical identifier.
    /// </summary>
    public bool HasCanonicalId => !string.IsNullOrWhiteSpace(CanonicalId);

    /// <summary>
    /// Gets or sets the origin system for the current canonical snapshot.
    /// </summary>
    public string? Origin { get; private set; }

    /// <summary>
    /// Timestamp of the latest canonization activity.
    /// </summary>
    public DateTimeOffset CanonizedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// External identifiers keyed by scheme.
    /// </summary>
    public Dictionary<string, CanonExternalId> ExternalIds
    {
        get => _externalIds;
        set => _externalIds = value is null
            ? new Dictionary<string, CanonExternalId>(StringComparer.OrdinalIgnoreCase)
            : value.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Source system attributions keyed by a contributor name.
    /// </summary>
    public Dictionary<string, CanonSourceAttribution> Sources
    {
        get => _sources;
        set => _sources = value is null
            ? new Dictionary<string, CanonSourceAttribution>(StringComparer.OrdinalIgnoreCase)
            : value.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Policy outcomes tracked during canonization.
    /// </summary>
    public Dictionary<string, CanonPolicySnapshot> Policies
    {
        get => _policies;
        set => _policies = value is null
            ? new Dictionary<string, CanonPolicySnapshot>(StringComparer.OrdinalIgnoreCase)
            : value.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Arbitrary tags recorded during the canonization lifecycle.
    /// </summary>
    public Dictionary<string, string> Tags
    {
        get => _tags;
        set => _tags = value is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lineage metadata linking related canonical entities.
    /// </summary>
    public CanonLineage Lineage
    {
        get => _lineage;
        set => _lineage = value ?? new CanonLineage();
    }

    /// <summary>
    /// Canon state snapshot aligned with the owning entity.
    /// </summary>
    public CanonState State
    {
        get => _state;
        set => _state = value?.Copy() ?? CanonState.Default;
    }

    /// <summary>
    /// Updates the canonical identifier associated with this metadata.
    /// </summary>
    /// <param name="canonicalId">Canonical identifier.</param>
    public void AssignCanonicalId(string canonicalId)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        lock (_gate)
        {
            CanonicalId = canonicalId;
            Touch();
        }
    }

    /// <summary>
    /// Updates the origin system for the metadata snapshot.
    /// </summary>
    /// <param name="origin">Origin system identifier.</param>
    public void SetOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new ArgumentException("Origin must be provided.", nameof(origin));
        }

        lock (_gate)
        {
            Origin = origin;
            Touch();
        }
    }

    /// <summary>
    /// Records or updates an external identifier.
    /// </summary>
    /// <param name="scheme">Identifier scheme (e.g., crm, sap).</param>
    /// <param name="value">Identifier value.</param>
    /// <param name="source">Optional source attribution key.</param>
    /// <param name="observedAt">Timestamp when the identifier was observed.</param>
    /// <param name="attributes">Optional attributes to attach.</param>
    public CanonExternalId RecordExternalId(string scheme, string value, string? source = null, DateTimeOffset? observedAt = null, IReadOnlyDictionary<string, string?>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            throw new ArgumentException("Scheme must be provided.", nameof(scheme));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier value must be provided.", nameof(value));
        }

        var externalId = new CanonExternalId
        {
            Scheme = scheme,
            Value = value,
            ObservedAt = observedAt ?? DateTimeOffset.UtcNow,
            Source = source
        };

        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                externalId.Attributes[pair.Key] = pair.Value;
            }
        }

        lock (_gate)
        {
            _externalIds[scheme] = externalId;
            Touch();
        }

        return externalId;
    }

    /// <summary>
    /// Attempts to retrieve an external identifier by scheme.
    /// </summary>
    public bool TryGetExternalId(string scheme, [NotNullWhen(true)] out CanonExternalId? externalId)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            externalId = null;
            return false;
        }

        lock (_gate)
        {
            return _externalIds.TryGetValue(scheme, out externalId);
        }
    }

    /// <summary>
    /// Removes an external identifier by scheme.
    /// </summary>
    public bool RemoveExternalId(string scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return false;
        }

        lock (_gate)
        {
            if (_externalIds.Remove(scheme))
            {
                Touch();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records a source attribution entry.
    /// </summary>
    /// <param name="sourceKey">Source key (e.g., adapter or integration name).</param>
    /// <param name="configure">Optional delegate to mutate the attribution.</param>
    public CanonSourceAttribution RecordSource(string sourceKey, Action<CanonSourceAttribution>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            throw new ArgumentException("Source key must be provided.", nameof(sourceKey));
        }

        lock (_gate)
        {
            if (!_sources.TryGetValue(sourceKey, out var attribution))
            {
                attribution = new CanonSourceAttribution
                {
                    Key = sourceKey,
                    SeenAt = DateTimeOffset.UtcNow
                };
                _sources[sourceKey] = attribution;
            }

            configure?.Invoke(attribution);
            attribution.SeenAt = DateTimeOffset.UtcNow;
            Touch();
            return attribution;
        }
    }

    /// <summary>
    /// Records a policy outcome.
    /// </summary>
    /// <param name="policy">Policy snapshot.</param>
    public void RecordPolicy(CanonPolicySnapshot policy)
    {
        if (policy is null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        lock (_gate)
        {
            _policies[policy.Policy] = policy.Clone();
            Touch();
        }
    }

    /// <summary>
    /// Sets or replaces a metadata tag.
    /// </summary>
    public void SetTag(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Tag key must be provided.", nameof(key));
        }

        lock (_gate)
        {
            _tags[key] = value;
            Touch();
        }
    }

    /// <summary>
    /// Attempts to retrieve the value of a metadata tag.
    /// </summary>
    public bool TryGetTag(string key, [NotNullWhen(true)] out string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = null;
            return false;
        }

        lock (_gate)
        {
            return _tags.TryGetValue(key, out value);
        }
    }

    /// <summary>
    /// Removes a tag entry.
    /// </summary>
    public bool RemoveTag(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_gate)
        {
            if (_tags.Remove(key))
            {
                Touch();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Updates the internal timestamp.
    /// </summary>
    public void Touch()
    {
        CanonizedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Clones the metadata instance into a detached copy.
    /// </summary>
    public CanonMetadata Clone()
    {
        lock (_gate)
        {
            return new CanonMetadata
            {
                CanonicalId = CanonicalId,
                Origin = Origin,
                CanonizedAt = CanonizedAt,
                ExternalIds = _externalIds.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.Clone(), StringComparer.OrdinalIgnoreCase),
                Sources = _sources.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.Clone(), StringComparer.OrdinalIgnoreCase),
                Policies = _policies.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.Clone(), StringComparer.OrdinalIgnoreCase),
                Tags = new Dictionary<string, string>(_tags, StringComparer.OrdinalIgnoreCase),
                Lineage = _lineage.Clone(),
                State = _state.Copy()
            };
        }
    }

    /// <summary>
    /// Merges another metadata instance into this one.
    /// </summary>
    /// <param name="incoming">Incoming metadata.</param>
    /// <param name="preferIncoming">If true, incoming values override existing ones.</param>
    public void Merge(CanonMetadata incoming, bool preferIncoming = true)
    {
        if (incoming is null)
        {
            throw new ArgumentNullException(nameof(incoming));
        }

        lock (_gate)
        {
            if (preferIncoming || CanonicalId is null)
            {
                CanonicalId = incoming.CanonicalId ?? CanonicalId;
            }

            if (preferIncoming || Origin is null)
            {
                Origin = incoming.Origin ?? Origin;
            }

            if (incoming.CanonizedAt > CanonizedAt)
            {
                CanonizedAt = incoming.CanonizedAt;
            }

            foreach (var pair in incoming._externalIds)
            {
                _externalIds[pair.Key] = preferIncoming
                    ? pair.Value.Clone()
                    : _externalIds.GetValueOrDefault(pair.Key, pair.Value.Clone());
            }

            foreach (var pair in incoming._sources)
            {
                if (preferIncoming || !_sources.ContainsKey(pair.Key))
                {
                    _sources[pair.Key] = pair.Value.Clone();
                }
            }

            foreach (var pair in incoming._policies)
            {
                if (preferIncoming || !_policies.ContainsKey(pair.Key))
                {
                    _policies[pair.Key] = pair.Value.Clone();
                }
            }

            foreach (var pair in incoming._tags)
            {
                if (preferIncoming || !_tags.ContainsKey(pair.Key))
                {
                    _tags[pair.Key] = pair.Value;
                }
            }

            _lineage.Merge(incoming._lineage, preferIncoming);

            _state = preferIncoming
                ? incoming._state.Copy()
                : _state.Merge(incoming._state, preferIncoming: false);
        }
    }
}