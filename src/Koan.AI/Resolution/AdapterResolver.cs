using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;

namespace Koan.AI.Resolution;

/// <summary>
/// Universal adapter resolution for all AI operations.
/// One pattern: query capability -> find adapter -> resolve or throw.
/// </summary>
public static class AdapterResolver
{
    /// <summary>
    /// Find the single adapter that has the given capability.
    /// If target is specified, find that specific adapter.
    /// Throws if zero or ambiguous.
    /// </summary>
    public static IAiAdapter Resolve(
        IAiAdapterRegistry registry,
        string capability,
        string? target = null)
    {
        if (target is not null)
        {
            var specific = registry.Get(target);
            if (specific is null)
            {
                var available = registry.All.Select(a => a.Id);
                throw new InvalidOperationException(
                    $"Adapter '{target}' not registered. Available: [{string.Join(", ", available)}]");
            }

            if (!specific.HasCapability(capability))
            {
                throw new InvalidOperationException(
                    $"Adapter '{target}' does not have '{capability}' capability. " +
                    $"Its capabilities: [{string.Join(", ", specific.Capabilities)}]");
            }

            return specific;
        }

        var candidates = registry.All
            .Where(a => a.HasCapability(capability))
            .ToList();

        return candidates.Count switch
        {
            0 => throw new InvalidOperationException(
                $"No adapter with '{capability}' capability. " +
                $"Registered adapters: [{string.Join(", ", registry.All.Select(a => $"{a.Id} ({string.Join(",", a.Capabilities)})"))}]"),
            1 => candidates[0],
            _ => throw new AmbiguousAdapterException(capability, candidates)
        };
    }

    /// <summary>
    /// Find all adapters with the given capability (for listing, aggregation).
    /// </summary>
    public static IReadOnlyList<IAiAdapter> ResolveAll(
        IAiAdapterRegistry registry,
        string capability)
    {
        return registry.All.Where(a => a.HasCapability(capability)).ToList();
    }
}
