namespace Koan.Core.Naming;

/// <summary>
/// One ordered contribution to a composed identifier: a raw axis value plus where it sits among the particles.
/// A <see langword="readonly"/> <see langword="struct"/> so a small particle set composes with no heap allocation.
/// </summary>
public readonly struct Particle
{
    public Particle(int order, string axis, string? value)
    {
        Order = order;
        Axis = axis ?? throw new ArgumentNullException(nameof(axis));
        Value = value;
    }

    /// <summary>Deterministic placement among particles — lower first; ties broken by <see cref="Axis"/> (ordinal).</summary>
    public int Order { get; }

    /// <summary>Which axis produced this particle (e.g. "partition", "tenant", "id").</summary>
    public string Axis { get; }

    /// <summary>The raw axis value, pre-format. A null/empty token after formatting omits the particle.</summary>
    public string? Value { get; }
}
