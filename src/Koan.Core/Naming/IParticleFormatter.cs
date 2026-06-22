namespace Koan.Core.Naming;

/// <summary>
/// Renders a raw axis value into an identifier-safe token for <see cref="IdentifierComposer"/>. A consumer
/// (data storage names, cache keys, ...) supplies the formatter via its <see cref="CompositionPolicy"/>, so the
/// same axis (e.g. partition) renders by <em>that</em> consumer's rules — adapter-capability sanitization for a
/// storage name, a fixed sentinel for a cache key. Returning <c>null</c> or empty <b>omits</b> the particle.
/// </summary>
public interface IParticleFormatter
{
    /// <summary>Render <paramref name="value"/> into a token, or return null/empty to omit the particle.</summary>
    string? Format(string? value);
}
