using System.Collections.Immutable;

namespace Koan.AI.Providers;

internal sealed class AiProviderPlanBuilder
{
    private readonly List<PendingProvider> _providers = [];
    private readonly Dictionary<string, int> _ownerOrder = new(StringComparer.OrdinalIgnoreCase);
    private AiProviderPlan? _compiled;

    internal AiProviderContributionTarget ForOwner(string owner)
    {
        EnsureMutable();
        var ownerId = Normalize(owner, "AI provider contribution owner");
        if (!_ownerOrder.ContainsKey(ownerId)) _ownerOrder.Add(ownerId, _ownerOrder.Count);
        return new AiProviderContributionTarget(this, ownerId);
    }

    internal void Add(string owner, string id, Type activatorType)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(activatorType);
        if (!typeof(IAiProviderActivator).IsAssignableFrom(activatorType))
        {
            throw new ArgumentException(
                $"AI provider activator '{activatorType.FullName}' must implement {nameof(IAiProviderActivator)}.",
                nameof(activatorType));
        }

        var ownerId = Normalize(owner, "AI provider contribution owner");
        if (!_ownerOrder.TryGetValue(ownerId, out var order))
        {
            throw new InvalidOperationException(
                $"AI provider contribution owner '{ownerId}' was not bound through {nameof(ForOwner)}.");
        }

        _providers.Add(new PendingProvider(
            ownerId,
            order,
            Normalize(id, "AI provider identity"),
            activatorType));
    }

    internal AiProviderPlan Build()
    {
        if (_compiled is not null) return _compiled;

        var duplicateId = _providers
            .GroupBy(static provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            var owners = string.Join(", ", duplicateId.Select(static provider => provider.Owner));
            throw new InvalidOperationException(
                $"AI provider id '{duplicateId.Key}' is declared more than once by: {owners}. " +
                "One provider identity must describe exactly one activator.");
        }

        var duplicateType = _providers
            .GroupBy(static provider => provider.ActivatorType)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateType is not null)
        {
            throw new InvalidOperationException(
                $"AI provider activator '{duplicateType.Key.FullName}' was contributed more than once.");
        }

        var registrations = _providers
            .OrderBy(static provider => provider.OwnerOrder)
            .ThenBy(static provider => provider.Id, StringComparer.Ordinal)
            .Select(static provider => new AiProviderRegistration(
                provider.Owner,
                provider.Id,
                provider.ActivatorType))
            .ToImmutableArray();

        _compiled = registrations.Length == 0
            ? AiProviderPlan.Empty
            : new AiProviderPlan(registrations);
        return _compiled;
    }

    private void EnsureMutable()
    {
        if (_compiled is not null)
        {
            throw new InvalidOperationException(
                "The AI provider plan is already compiled and cannot accept more providers.");
        }
    }

    private static string Normalize(string? value, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 256 || normalized.Any(static character =>
                !char.IsLetterOrDigit(character)
                && character is not '.' and not '-' and not '_' and not ':'))
        {
            throw new ArgumentException(
                $"{label} may contain only letters, numbers, '.', '-', '_', or ':' and must be at most 256 characters.");
        }

        return normalized;
    }

    private sealed record PendingProvider(
        string Owner,
        int OwnerOrder,
        string Id,
        Type ActivatorType);
}

internal sealed class AiProviderPlan
{
    internal static AiProviderPlan Empty { get; } = new([]);

    internal AiProviderPlan(ImmutableArray<AiProviderRegistration> providers)
    {
        Providers = providers;
    }

    internal ImmutableArray<AiProviderRegistration> Providers { get; }
}

internal sealed record AiProviderRegistration(string Owner, string Id, Type ActivatorType);
