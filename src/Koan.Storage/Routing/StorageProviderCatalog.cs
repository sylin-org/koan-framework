using Koan.Core.Capabilities;
using Koan.Core.Providers;
using Koan.Storage.Abstractions;
using Koan.Storage.Abstractions.Capabilities;

namespace Koan.Storage.Routing;

/// <summary>Host-owned Storage provider identities, capabilities, placement, and deterministic rank.</summary>
internal sealed class StorageProviderCatalog
{
    private readonly ProviderCatalog<IStorageProvider> _providers;
    private readonly IReadOnlyDictionary<IStorageProvider, StorageProviderCandidate> _descriptions;

    public StorageProviderCatalog(IEnumerable<IStorageProvider> providers)
    {
        _providers = ProviderCatalog<IStorageProvider>.Compile(providers, DescribeProvider);
        var descriptions = new Dictionary<IStorageProvider, StorageProviderCandidate>(ReferenceEqualityComparer.Instance);
        foreach (var candidate in _providers.Candidates)
            descriptions.Add(candidate.Value, CompileCandidate(candidate));
        _descriptions = descriptions;
        Candidates = _providers.Candidates.Select(candidate => _descriptions[candidate.Value]).ToArray();
    }

    public IReadOnlyList<StorageProviderCandidate> Candidates { get; }

    public StorageProviderCandidate? Find(string? identity)
    {
        var provider = _providers.Find(identity);
        return provider is null ? null : _descriptions[provider];
    }

    public StorageProviderCandidate? Best(StorageProviderPlacement placement)
    {
        var provider = _providers.Best(
            _providers.Candidates.Where(candidate => candidate.Value.Placement == placement),
            static (left, right) => right.Priority.CompareTo(left.Priority));
        return provider is null ? null : _descriptions[provider];
    }

    private static ProviderCandidateDescriptor DescribeProvider(IStorageProvider provider)
        => new(
            provider.Name,
            referenceIdentities: ProviderMetadata.References(provider.GetType()),
            priority: ProviderMetadata.Priority(provider.GetType()));

    private static StorageProviderCandidate CompileCandidate(ProviderCandidate<IStorageProvider> candidate)
    {
        var capabilities = StorageCaps.Describe(candidate.Value, candidate.Id);
        RequireAlignment(candidate, capabilities, StorageCaps.PresignedRead, candidate.Value is IPresignOperations);
        RequireAlignment(candidate, capabilities, StorageCaps.PresignedWrite, candidate.Value is IPresignOperations);
        RequireAlignment(candidate, capabilities, StorageCaps.ServerSideCopy, candidate.Value is IServerSideCopy);
        RequireAlignment(candidate, capabilities, StorageCaps.Stat, candidate.Value is IStatOperations);
        RequireAlignment(candidate, capabilities, StorageCaps.List, candidate.Value is IListOperations);

        return new StorageProviderCandidate(
            candidate.Value,
            candidate.Id,
            candidate.Value.Placement,
            candidate.Priority,
            capabilities);
    }

    private static void RequireAlignment(
        ProviderCandidate<IStorageProvider> candidate,
        CapabilitySet capabilities,
        Capability capability,
        bool implementsOperation)
    {
        if (capabilities.Has(capability) == implementsOperation) return;

        var correction = implementsOperation
            ? $"declare '{capability.Id}' in Describe"
            : $"remove '{capability.Id}' from Describe or implement its operation contract";
        throw new InvalidOperationException(
            $"Storage provider '{candidate.Id}' has inconsistent capability '{capability.Id}'; {correction}.");
    }
}

internal sealed record StorageProviderCandidate(
    IStorageProvider Provider,
    string Id,
    StorageProviderPlacement Placement,
    int Priority,
    CapabilitySet Capabilities);
