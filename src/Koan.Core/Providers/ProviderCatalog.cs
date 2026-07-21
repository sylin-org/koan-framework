using System.ComponentModel;
using Koan.Core.Composition;
using Koan.Core.Semantics;

namespace Koan.Core.Providers;

/// <summary>
/// Immutable host-owned provider identity catalog. It centralizes only mechanics whose meaning is
/// framework-wide; callers supply candidate activation, eligibility, ranking, reason, and correction.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProviderCatalog<TProvider> where TProvider : class
{
    private readonly IReadOnlyDictionary<string, ProviderCandidate<TProvider>> _byIdentity;
    private readonly IReadOnlyDictionary<TProvider, ProviderCandidate<TProvider>> _byProvider;

    private ProviderCatalog(
        IReadOnlyList<ProviderCandidate<TProvider>> candidates,
        IReadOnlyDictionary<string, ProviderCandidate<TProvider>> byIdentity,
        IReadOnlyDictionary<TProvider, ProviderCandidate<TProvider>> byProvider)
    {
        Candidates = candidates;
        _byIdentity = byIdentity;
        _byProvider = byProvider;
    }

    public IReadOnlyList<ProviderCandidate<TProvider>> Candidates { get; }

    public static ProviderCatalog<TProvider> Compile(
        IEnumerable<TProvider> providers,
        Func<TProvider, ProviderCandidateDescriptor> describe)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(describe);

        var compiled = new List<ProviderCandidate<TProvider>>();
        foreach (var provider in providers)
        {
            if (provider is null)
            {
                throw new InvalidOperationException("Provider catalogs cannot contain a null provider.");
            }

            var descriptor = describe(provider)
                ?? throw new InvalidOperationException(
                    $"Provider '{provider.GetType().FullName}' returned no candidate descriptor.");
            var id = NormalizeIdentity(descriptor.Id, "Provider identities cannot be empty.");
            var aliases = descriptor.Aliases
                .Select(alias => NormalizeIdentity(alias, $"Provider '{id}' declares an empty alias."))
                .Where(alias => !string.Equals(alias, id, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var referenceIdentities = descriptor.ReferenceIdentities
                .Select(reference => NormalizeReference(reference, id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray();

            compiled.Add(new ProviderCandidate<TProvider>(
                provider,
                id,
                aliases,
                referenceIdentities,
                descriptor.Priority));
        }

        var ordered = compiled
            .OrderBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
        var byIdentity = new Dictionary<string, ProviderCandidate<TProvider>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in ordered)
        {
            if (!byIdentity.TryAdd(candidate.Id, candidate))
            {
                throw new InvalidOperationException(
                    $"Provider identity '{candidate.Id}' is declared more than once. Provider identities must be unique within one typed catalog.");
            }
        }

        foreach (var candidate in ordered)
        {
            foreach (var alias in candidate.Aliases)
            {
                if (byIdentity.TryGetValue(alias, out var existing))
                {
                    var collision = string.Equals(existing.Id, alias, StringComparison.OrdinalIgnoreCase)
                        ? $"Provider identity '{existing.Id}' is also declared as an alias by '{candidate.Id}'."
                        : $"Provider alias '{alias}' is declared by both '{existing.Id}' and '{candidate.Id}'.";
                    throw new InvalidOperationException(
                        $"{collision} Provider identities and aliases must resolve to exactly one candidate.");
                }

                byIdentity.Add(alias, candidate);
            }
        }

        var byProvider = new Dictionary<TProvider, ProviderCandidate<TProvider>>(
            ReferenceEqualityComparer.Instance);
        foreach (var candidate in ordered)
        {
            if (!byProvider.TryAdd(candidate.Value, candidate))
            {
                throw new InvalidOperationException(
                    $"Provider instance '{candidate.Value.GetType().FullName}' was added to the catalog more than once.");
            }
        }

        return new ProviderCatalog<TProvider>(ordered, byIdentity, byProvider);
    }

    public TProvider? Find(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity)) return null;
        return _byIdentity.TryGetValue(identity.Trim(), out var candidate)
            ? candidate.Value
            : null;
    }

    public ProviderCandidate<TProvider> Describe(TProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _byProvider.TryGetValue(provider, out var candidate)
            ? candidate
            : throw new InvalidOperationException(
                $"Provider '{provider.GetType().FullName}' does not belong to this host catalog.");
    }

    public IReadOnlyList<TProvider> Direct(KoanApplicationReferenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.IsPresent) return [];

        return Candidates
            .Where(candidate => candidate.ReferenceIdentities.Any(identity =>
                manifest.DirectReferences.Any(reference =>
                    string.Equals(identity, reference.RawIdentity, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(identity, reference.CanonicalIdentity, StringComparison.OrdinalIgnoreCase))))
            .Select(static candidate => candidate.Value)
            .ToArray();
    }

    public TProvider? Best(
        IEnumerable<ProviderCandidate<TProvider>> candidates,
        Comparison<ProviderCandidate<TProvider>> comparison)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(comparison);

        return candidates
            .OrderBy(static candidate => candidate, Comparer<ProviderCandidate<TProvider>>.Create(comparison))
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .Select(static candidate => candidate.Value)
            .FirstOrDefault();
    }

    private static string NormalizeIdentity(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(emptyMessage);
        try
        {
            return new SemanticId(value).Value;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Provider identity '{value}' is invalid. Use a stable identifier containing only letters, numbers, '.', '-', '_', or ':'.",
                exception);
        }
    }

    private static string NormalizeReference(string? value, string providerId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Provider '{providerId}' declares an empty direct-reference identity.");
        }

        return value.Trim();
    }
}
