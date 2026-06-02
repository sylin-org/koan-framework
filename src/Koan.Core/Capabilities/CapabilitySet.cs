namespace Koan.Core.Capabilities;

/// <summary>
/// The set of capabilities a provider declares: built once through the <see cref="ICapabilities"/>
/// face, then negotiated against (<see cref="Has"/> / <see cref="Require"/> / <see cref="Detail{TDetail}"/>)
/// and reported (<see cref="All"/>). This one type replaces the ~40 ad-hoc capability enums,
/// records, and marker interfaces the framework accreted. See ARCH-0084.
/// </summary>
public sealed class CapabilitySet : ICapabilities
{
    private readonly Dictionary<Capability, object?> _tokens = new();

    /// <summary>Creates an empty set, optionally tagged with the declaring provider's id.</summary>
    public CapabilitySet(string? owner = null) => Owner = owner;

    /// <summary>
    /// Optional id of the declaring provider (e.g. <c>"data.postgres"</c>). Used only to make
    /// <see cref="CapabilityNotSupportedException"/> messages legible.
    /// </summary>
    public string? Owner { get; }

    /// <inheritdoc />
    public ICapabilities Add(Capability token)
    {
        _tokens[token] = null;
        return this;
    }

    /// <inheritdoc />
    public ICapabilities Add<TDetail>(Capability token, TDetail detail) where TDetail : notnull
    {
        _tokens[token] = detail;
        return this;
    }

    /// <summary>True when the provider declared the capability.</summary>
    public bool Has(Capability token) => _tokens.ContainsKey(token);

    /// <summary>
    /// Throws <see cref="CapabilityNotSupportedException"/> when the capability was not declared —
    /// the single fail-loud path for negotiation.
    /// </summary>
    public void Require(Capability token)
    {
        if (!_tokens.ContainsKey(token))
            throw new CapabilityNotSupportedException(token, Owner);
    }

    /// <summary>
    /// The structured detail attached to a token, or <c>default</c> when the token is absent or
    /// carries a different detail type.
    /// </summary>
    public TDetail? Detail<TDetail>(Capability token)
        => _tokens.TryGetValue(token, out var detail) && detail is TDetail typed ? typed : default;

    /// <summary>Every declared capability — the uniform self-report surface.</summary>
    public IReadOnlyCollection<Capability> All => _tokens.Keys;

    /// <summary>
    /// Copies every declared capability — each token and any attached structured detail — onto
    /// <paramref name="target"/>. Used by repository decorators to forward an inner provider's
    /// resolved capabilities through their own <see cref="IDescribesCapabilities.Describe"/>.
    /// </summary>
    public void CopyInto(ICapabilities target)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (var (token, detail) in _tokens)
        {
            if (detail is null) target.Add(token);
            else target.Add(token, detail);
        }
    }

    /// <summary>
    /// Convenience builder: <c>CapabilitySet.Build("data.postgres", c =&gt; c.Add(DataCaps.Query.Linq))</c>.
    /// </summary>
    public static CapabilitySet Build(string? owner, Action<ICapabilities> declare)
    {
        ArgumentNullException.ThrowIfNull(declare);
        var set = new CapabilitySet(owner);
        declare(set);
        return set;
    }
}
