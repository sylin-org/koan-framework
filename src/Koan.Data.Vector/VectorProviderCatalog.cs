using Koan.Core.Composition;
using Koan.Core.Providers;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

/// <summary>Host-owned vector-provider catalog and automatic-candidate policy.</summary>
internal sealed class VectorProviderCatalog : IVectorProviderResolver
{
    private readonly ProviderCatalog<IVectorAdapterFactory> _catalog;
    private readonly KoanApplicationReferenceManifest? _references;
    private readonly Lazy<IVectorAdapterFactory?> _automatic;

    public VectorProviderCatalog(
        IEnumerable<IVectorAdapterFactory> factories,
        KoanApplicationReferenceManifest? references)
    {
        _references = references;
        _catalog = ProviderCatalog<IVectorAdapterFactory>.Compile(factories, DescribeFactory);
        _automatic = new Lazy<IVectorAdapterFactory?>(
            SelectAutomaticCore,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<ProviderCandidate<IVectorAdapterFactory>> Candidates => _catalog.Candidates;
    public IReadOnlyList<string> AvailableProviderIds => Candidates.Select(static candidate => candidate.Id).ToArray();

    public IVectorAdapterFactory? Find(string? identity) => _catalog.Find(identity);

    public IVectorAdapterFactory? SelectAutomatic() => _automatic.Value;

    private IVectorAdapterFactory? SelectAutomaticCore()
    {
        if (Candidates.Count == 0) return null;
        if (_references?.IsPresent == true)
        {
            var direct = _catalog.Direct(_references).Select(_catalog.Describe).ToArray();
            if (direct.Length > 0) return Best(direct);

            var floors = Candidates.Where(static candidate => candidate.Value.IsAutomaticFloor).ToArray();
            return floors.Length == 0 ? null : Best(floors);
        }

        return Best(Candidates);
    }

    public ProviderCandidate<IVectorAdapterFactory> Describe(IVectorAdapterFactory factory) =>
        _catalog.Describe(factory);

    private IVectorAdapterFactory? Best(IEnumerable<ProviderCandidate<IVectorAdapterFactory>> candidates) =>
        _catalog.Best(candidates, static (left, right) => right.Priority.CompareTo(left.Priority));

    private static ProviderCandidateDescriptor DescribeFactory(IVectorAdapterFactory factory) => new(
        factory.Provider,
        factory.Aliases.ToArray(),
        factory.ReferenceIdentities
            .Concat(ProviderMetadata.References(factory.GetType()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        ProviderMetadata.Priority(factory.GetType()));
}
