using Koan.Core.Composition;
using Koan.Core.Providers;
using Koan.Data.Abstractions;
using System.ComponentModel;

namespace Koan.Data.Core.Routing;

/// <summary>Host-owned record-provider catalog and Data's automatic-candidate policy.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DataProviderCatalog
{
    private readonly ProviderCatalog<IDataAdapterFactory> _catalog;
    private readonly KoanApplicationReferenceManifest? _references;
    private readonly Lazy<DataProviderSelection> _automatic;

    public DataProviderCatalog(
        IEnumerable<IDataAdapterFactory> factories,
        KoanApplicationReferenceManifest? references)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _references = references;
        _catalog = ProviderCatalog<IDataAdapterFactory>.Compile(factories, DescribeFactory);
        _automatic = new Lazy<DataProviderSelection>(
            SelectAutomaticCore,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<ProviderCandidate<IDataAdapterFactory>> Candidates => _catalog.Candidates;

    public IDataAdapterFactory? Find(string? identity) => _catalog.Find(identity);

    public ProviderCandidate<IDataAdapterFactory> Describe(IDataAdapterFactory factory) =>
        _catalog.Describe(factory);

    public DataProviderSelection SelectAutomatic() => _automatic.Value;

    public DataProviderSelection Require(
        string identity,
        string subject,
        string reason,
        string correction)
    {
        var factory = Find(identity);
        if (factory is null)
        {
            var available = Candidates.Select(static candidate => candidate.Id).ToArray();
            var choices = available.Length == 0 ? "no referenced adapters" : string.Join(", ", available);
            throw new AdapterResolutionException(
                identity,
                Infrastructure.Constants.Diagnostics.Reasons.AdapterUnavailable,
                $"{correction} Available providers: {choices}.");
        }

        var candidate = Describe(factory);
        return new DataProviderSelection(
            factory,
            new ProviderSelectionReceipt(
                subject,
                candidate.Id,
                ProviderIntentPosture.Required,
                candidate.Priority,
                reason));
    }

    private DataProviderSelection SelectAutomaticCore()
    {
        if (Candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "Koan Data has no provider candidates. Reference a Data connector and call AddKoan().");
        }

        if (_references?.IsPresent == true)
        {
            var direct = _catalog.Direct(_references)
                .Select(_catalog.Describe)
                .ToArray();
            if (direct.Length > 0)
            {
                return Decision(
                    Best(direct),
                    "direct-reference-intent",
                    directIntent: true);
            }

            var floors = Candidates
                .Where(static candidate => candidate.Value.IsAutomaticFloor)
                .ToArray();
            if (floors.Length > 0)
            {
                return Decision(
                    Best(floors),
                    "built-in-floor",
                    directIntent: false);
            }

            throw new InvalidOperationException(
                "Koan Data found no directly referenced connector or bundle-provided automatic floor. " +
                "Reference the intended Data connector or configure an exact Data source adapter.");
        }

        return Decision(
            Best(Candidates),
            "unknown-provenance-priority",
            directIntent: false);
    }

    private IDataAdapterFactory Best(IEnumerable<ProviderCandidate<IDataAdapterFactory>> candidates) =>
        _catalog.Best(candidates, ComparePriority)
        ?? throw new InvalidOperationException("Koan Data has no eligible provider candidate.");

    private DataProviderSelection Decision(
        IDataAdapterFactory factory,
        string via,
        bool directIntent)
    {
        var candidate = _catalog.Describe(factory);
        return new DataProviderSelection(
            factory,
            new ProviderSelectionReceipt(
                "data:default",
                candidate.Id,
                ProviderIntentPosture.Automatic,
                candidate.Priority,
                via,
                directIntent));
    }

    private static ProviderCandidateDescriptor DescribeFactory(IDataAdapterFactory factory)
    {
        var references = factory.ReferenceIdentities
            .Concat(ProviderMetadata.References(factory.GetType()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ProviderCandidateDescriptor(
            factory.Provider,
            factory.Aliases.ToArray(),
            references,
            ProviderMetadata.Priority(factory.GetType()));
    }

    private static int ComparePriority(
        ProviderCandidate<IDataAdapterFactory> left,
        ProviderCandidate<IDataAdapterFactory> right) =>
        right.Priority.CompareTo(left.Priority);
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DataProviderSelection(
    IDataAdapterFactory Factory,
    ProviderSelectionReceipt Receipt)
{
    public string ProviderId => Receipt.ProviderId;
    public string Via => Receipt.Reason;
    public int Priority => Receipt.Priority;
    public bool DirectIntent => Receipt.DirectIntent;
}
