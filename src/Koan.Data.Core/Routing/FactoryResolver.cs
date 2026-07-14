using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Routing;

/// <summary>
/// The shared <c>[ProviderPriority]</c> + <see cref="IAdapterFactory.CanHandle"/> ranking for any adapter-factory
/// family (ARCH-0103 §4.1). The single selection rule the record plane (<c>AdapterResolver</c>) and the vector plane
/// (<c>VectorService</c>) both use, so the two factory families resolve a provider identically instead of carrying two
/// hand-kept-in-sync copies of the ranking.
/// </summary>
public static class FactoryResolver
{
    /// <summary>
    /// Pick the factory that handles <paramref name="desired"/> — preferring the highest <c>[ProviderPriority]</c>
    /// (type-name as the deterministic tie-break) — or, when <paramref name="desired"/> is blank, the highest-priority
    /// factory overall. Returns <c>null</c> when none match or the list is empty.
    /// </summary>
    public static TFactory? Resolve<TFactory>(IEnumerable<TFactory> factories, string? desired)
        where TFactory : class, IAdapterFactory
    {
        var ranked = Rank(factories);
        if (!string.IsNullOrWhiteSpace(desired))
            return ranked.FirstOrDefault(f => f.CanHandle(desired)) ?? ranked.FirstOrDefault();
        return ranked.FirstOrDefault();
    }

    /// <summary>
    /// The provider name a factory contributes when chosen by priority — the class-name convention
    /// ("SqliteAdapterFactory" → "sqlite"), preserved verbatim from the prior <c>AdapterResolver</c>/<c>VectorService</c>
    /// derivation so the priority fallback stays byte-identical.
    /// </summary>
    public static string ProviderName<TFactory>(TFactory factory) where TFactory : class, IAdapterFactory
    {
        const string suffix = "AdapterFactory";
        var name = factory.GetType().Name;
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
        return name.ToLowerInvariant();
    }

    internal static int Priority<TFactory>(TFactory factory) where TFactory : class, IAdapterFactory
        => factory.GetType().GetCustomAttribute<ProviderPriorityAttribute>()?.Priority ?? 0;

    // Highest [ProviderPriority] first; type-name (ordinal-ignore-case) as the stable tie-break — the exact ordering
    // both call sites used before convergence.
    private static IReadOnlyList<TFactory> Rank<TFactory>(IEnumerable<TFactory> factories)
        where TFactory : class, IAdapterFactory
        => factories
            .OrderByDescending(f => f.GetType().GetCustomAttribute<ProviderPriorityAttribute>()?.Priority ?? 0)
            .ThenBy(f => f.GetType().Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
