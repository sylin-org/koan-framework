using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Infrastructure;

namespace Koan.AI.Sources;

internal sealed class AiSourceControl(
    IAiSourceRuntimeRegistry sources,
    IAiAdapterRegistry adapters,
    AiProvenancePublisher provenance) : IAiSourceControl
{
    public Task<AiSourceInspection> InspectAsync(
        AiSourceCandidate candidate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(candidate.Provider))
            throw new ArgumentException("AI source inspection requires a provider.", nameof(candidate));
        if (!Uri.TryCreate(candidate.Endpoint, UriKind.Absolute, out _))
            throw new ArgumentException(
                $"AI source endpoint '{candidate.Endpoint}' is not an absolute URI.",
                nameof(candidate));

        var adapter = adapters.Get(candidate.Provider)
            ?? throw new InvalidOperationException(
                $"AI provider '{candidate.Provider}' is not active. Reference its Koan connector package " +
                $"before inspecting the endpoint. Active providers: {string.Join(", ", adapters.All.Select(a => a.Id))}.");

        if (adapter is not IAiSourceInspector inspector)
        {
            throw new InvalidOperationException(
                $"AI provider '{candidate.Provider}' does not support endpoint inspection. " +
                "Use a provider connector that implements IAiSourceInspector or register a source explicitly.");
        }

        return inspector.InspectAsync(candidate, ct);
    }

    public AiSourceDefinition Apply(AiSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var applied = sources.Apply(source);
        Republish();
        return applied;
    }

    public bool Enable(string name) => SetEnabled(name, enabled: true);

    public bool Disable(string name) => SetEnabled(name, enabled: false);

    public bool Remove(string name, string? expectedOrigin = null)
    {
        ValidateName(name);
        var removed = sources.Remove(name, expectedOrigin);
        if (removed) Republish();
        return removed;
    }

    private bool SetEnabled(string name, bool enabled)
    {
        ValidateName(name);
        var changed = sources.SetEnabled(name, enabled);
        if (changed) Republish();
        return changed;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("AI source name cannot be empty.", nameof(name));
    }

    private void Republish() => _ = provenance.Publish(CancellationToken.None);
}
