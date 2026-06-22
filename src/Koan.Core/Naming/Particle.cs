namespace Koan.Core.Naming;

/// <summary>Where a particle sits relative to the anchor in a composed identifier (ARCH-0096 realignment).</summary>
public enum ParticlePosition
{
    /// <summary>After the anchor — <c>anchor{sep}token</c> (e.g. the partition suffix, <c>Todo#alpha</c>). The default.</summary>
    Trailing = 0,

    /// <summary>Before the anchor — <c>token{sep}anchor</c> (e.g. a tenant container prefix, <c>2a6v7.Todo</c>).</summary>
    Leading = 1,
}

/// <summary>
/// One ordered contribution to a composed identifier: a raw axis value, where it sits relative to the anchor
/// (<see cref="Position"/>), and an optional per-particle <see cref="Separator"/> that overrides the policy default
/// (so a leading tenant container can join with <c>.</c> while a trailing partition joins with the adapter's token
/// separator). A <see langword="readonly"/> <see langword="struct"/> so a small particle set composes with no heap
/// allocation.
/// </summary>
public readonly struct Particle
{
    /// <summary>A trailing particle with the policy's default separator (the common case — preserves prior behavior).</summary>
    public Particle(int order, string axis, string? value)
        : this(order, axis, value, ParticlePosition.Trailing, separator: null) { }

    /// <summary>A particle with an explicit position and optional separator override (<c>null</c> = policy default).</summary>
    public Particle(int order, string axis, string? value, ParticlePosition position, string? separator = null)
    {
        Order = order;
        Axis = axis ?? throw new ArgumentNullException(nameof(axis));
        Value = value;
        Position = position;
        Separator = separator;
    }

    /// <summary>Deterministic placement among particles on the same side — lower first; ties broken by <see cref="Axis"/> (ordinal).</summary>
    public int Order { get; }

    /// <summary>Which axis produced this particle (e.g. "partition", "tenant", "id").</summary>
    public string Axis { get; }

    /// <summary>The raw axis value, pre-format. A null/empty token after formatting omits the particle.</summary>
    public string? Value { get; }

    /// <summary>Whether this particle leads (prefix) or trails (suffix) the anchor.</summary>
    public ParticlePosition Position { get; }

    /// <summary>The separator joining this particle to its neighbour; <c>null</c> uses the policy's default separator.</summary>
    public string? Separator { get; }
}
