namespace Koan.Core.Naming;

/// <summary>
/// The per-consumer rules <see cref="IdentifierComposer"/> applies: the <see cref="Separator"/> placed between
/// the anchor and each particle token, the <see cref="IParticleFormatter"/> that renders each particle, and an
/// optional UTF-8 byte limit (<see cref="MaxBytes"/>) — when the composed identifier exceeds it, the composer
/// keeps a readable anchor prefix and appends a deterministic hash. <b>One engine, per-consumer policy:</b> a
/// storage name supplies an adapter capability's separator + token policy + identifier limit; a cache key
/// supplies a fixed separator and no limit.
/// </summary>
public readonly struct CompositionPolicy
{
    public CompositionPolicy(string separator, IParticleFormatter formatter, int? maxBytes = null)
    {
        Separator = separator ?? throw new ArgumentNullException(nameof(separator));
        Formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        MaxBytes = maxBytes;
    }

    /// <summary>The string placed between the anchor and each particle token (and before the overflow hash).</summary>
    public string Separator { get; }

    /// <summary>How each particle's raw value is rendered (and whether it is omitted).</summary>
    public IParticleFormatter Formatter { get; }

    /// <summary>Maximum identifier length in UTF-8 bytes; <c>null</c> = unbounded.</summary>
    public int? MaxBytes { get; }
}
