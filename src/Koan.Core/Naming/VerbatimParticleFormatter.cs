namespace Koan.Core.Naming;

/// <summary>
/// An identity <see cref="IParticleFormatter"/> — renders a particle value verbatim (no sanitization). For
/// identifiers that are <b>opaque equality strings</b> (job coalesce keys, …), not physical storage names whose
/// adapter capability dictates a sanitizing token policy. A null/empty value still omits the particle.
/// </summary>
public sealed class VerbatimParticleFormatter : IParticleFormatter
{
    public static readonly VerbatimParticleFormatter Instance = new();
    private VerbatimParticleFormatter() { }
    public string? Format(string? value) => value;
}
