namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Records which vector providers have become runtime dependencies of the current host.
/// </summary>
/// <remarks>
/// Connector presence makes a provider available. The Vector pillar records participation only when
/// repository resolution selects that provider for an Entity and source. Health contributors use the
/// resulting host-owned snapshot; they do not infer criticality from package presence.
/// </remarks>
public interface IVectorAdapterParticipation
{
    /// <summary>Marks a provider/source pair as selected by a runtime vector operation.</summary>
    void Observe(string provider, string source);

    /// <summary>Returns the logical sources currently using <paramref name="provider"/>.</summary>
    IReadOnlyCollection<string> ActiveSources(string provider);
}
