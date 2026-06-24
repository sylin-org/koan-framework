using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The data-axis self-reporting surface (ARCH-0101 §9) — <see cref="Explain(IServiceProvider, Type)"/> renders the
/// whole isolation story for an entity in ONE place: which planes compose it, the read-scope predicate active in the
/// CURRENT ambient, whether the adapter can isolate it (fail-closed satisfaction), whether that predicate fully pushes
/// down, and whether the entity is cache-excluded. The magic is inspectable — a reviewer (or a query) reads the RSoP
/// instead of tracing the facade. It reuses the same registries + the one facade inspection authority
/// (<see cref="IAxisScopeDiagnostics"/>), so it can never drift from what a real read actually does.
/// </summary>
public static class DataAxis
{
    /// <summary>Explain the axis composition + read-scope of <typeparamref name="TEntity"/> in the current ambient.</summary>
    public static AxisExplanation Explain<TEntity>(IServiceProvider services) where TEntity : class
        => Explain(services, typeof(TEntity));

    /// <summary>Explain the axis composition + read-scope of <paramref name="entityType"/> in the current ambient. The
    /// planes come from the static registries; the active fold + adapter satisfaction from the facade diagnostic (best
    /// effort — if the adapter cannot be resolved, the registry-level story is still returned).</summary>
    public static AxisExplanation Explain(IServiceProvider services, Type entityType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entityType);

        var planes = new List<AxisPlane>();

        // Managed-field plane (the stamp + auto-equality read-filter).
        var managed = ManagedFieldRegistry.ForType(entityType);
        foreach (var d in managed)
            planes.Add(new AxisPlane("managed-field", d.StorageName,
                $"{d.ClrType.Name}; {(d.RequiredCapability is { } c ? c.Id : "no-isolation")}; " +
                $"{(d.Indexed ? "indexed" : "unindexed")}; {(d.AutoReadFilter ? "auto-equality read-filter" : "no auto read-filter")}"));

        // Container-name particle plane (only present in the current ambient when a value is in scope).
        foreach (var p in StorageNameParticleRegistry.Gather(entityType))
            planes.Add(new AxisPlane("name-particle", p.Axis, $"value='{p.Value}'; {p.Position}; sep='{p.Separator ?? "default"}'"));

        // Operation-semantics override plane (soft-delete).
        if (OperationOverrideRegistry.ForDelete(entityType) is { } ov)
            planes.Add(new AxisPlane("operation-override", ov.Field, $"OnDelete = {ov.OnDeleteValue}"));

        // Read-filter contributor plane (non-equality predicates; the built-in equality is folded via the managed fields).
        var contributors = services.GetServices<IReadFilterContributor>().ToArray();
        // The cache-exclusion mirrors CachedRepository EXACTLY (the diagnostic must not drift): a field transform
        // ([Classified]/encrypted), a non-equality managed field (AutoReadFilter==false), OR a contributor that excludes.
        var cacheExcluded = StorageFieldTransformRegistry.HasTransformsFor(entityType)
            || managed.Any(d => !d.AutoReadFilter);   // a non-equality managed field excludes from cache (DATA-0106 §5)
        foreach (var c in contributors)
        {
            if (c is ManagedEqualityReadContributor) continue;     // the built-in default is reported via the managed-field plane
            var active = c.ReadFilter(entityType) is not null;
            var excludes = c.ExcludesFromCache(entityType);
            cacheExcluded |= excludes;
            if (!active && !excludes) continue;                    // not applicable to this type ⇒ omit
            planes.Add(new AxisPlane("read-filter", ReadFilterLabel(c),
                $"{(active ? "ACTIVE now" : "inactive now")}; {(excludes ? "cache-excluded" : "cacheable")}; " +
                $"requires {(c.RequiredCapability is { } rc ? rc.Id : "no capability")}"));
        }

        // The active read-scope fold is registry/DI-driven (the SAME fold the facade applies) — available even without
        // a resolvable adapter, so the registry-level RSoP always stands.
        var readScope = ReadScopeFold.Fold(contributors, entityType);

        // Adapter fail-closed satisfaction, from the one facade inspection authority. Three honest outcomes (the
        // diagnostic must NEVER report a scoped entity as "not a leak" when it cannot verify isolation):
        //   • resolved        ⇒ the facade's real satisfaction;
        //   • no adapter context (no IDataService / not an entity) ⇒ registry-level RSoP, NO adapter claim (null);
        //   • a CONFIGURED route that fails to resolve ⇒ for a scopable entity, bias-to-strict: cannot-isolate.
        var (diag, adapterError) = ResolveDiagnostics(services, entityType);
        var couldScope = readScope is not null || planes.Any(p => p.Plane is "managed-field" or "read-filter");
        AxisAdapter? adapter =
            diag is not null ? new AxisAdapter(
                    diag.AdapterName,
                    diag.ScopeAdapterOk,
                    diag.ScopeAdapterOk ? null : diag.ScopeAdapterError,
                    readScope is null ? null : diag.IsFullyPushable(readScope))
            : adapterError is not null && couldScope ? new AxisAdapter(
                    "<unresolved>", IsolationSatisfied: false, FailClosedReason: adapterError, Pushable: null)
            : null;

        return new AxisExplanation(entityType, planes, readScope, cacheExcluded, adapter);
    }

    private static string ReadFilterLabel(IReadFilterContributor c)
        => c is DelegatingReadFilterContributor d ? $"axis:{d.AxisId}" : c.GetType().Name;

    // Resolve the facade diagnostic. Distinguishes the BENIGN absence (no IDataService / not an entity ⇒ no adapter
    // context, both null) from a CONFIGURED route that FAILS to resolve (error set) — the latter must never be swallowed
    // to a silent "safe", so a scopable entity on an unresolvable adapter reads as a leak (bias-to-strict).
    private static (IAxisScopeDiagnostics? diag, string? error) ResolveDiagnostics(IServiceProvider services, Type entityType)
    {
        if (services.GetService(typeof(IDataService)) is not IDataService dataService) return (null, null);
        var keyType = KeyTypeOf(entityType);
        if (keyType is null) return (null, null);
        try
        {
            var method = typeof(IDataService).GetMethod(nameof(IDataService.GetScopeDiagnostics))!
                .MakeGenericMethod(entityType, keyType);
            return (method.Invoke(dataService, null) as IAxisScopeDiagnostics, null);
        }
        catch (Exception ex)
        {
            var actual = (ex as TargetInvocationException)?.InnerException ?? ex;
            return (null, $"the adapter for '{entityType.Name}' could not be resolved ({actual.Message})");
        }
    }

    /// <summary>The <c>TKey</c> of an <see cref="IEntity{TKey}"/> implementer, or <c>null</c>.</summary>
    internal static Type? KeyTypeOf(Type entityType)
    {
        foreach (var i in entityType.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>))
                return i.GetGenericArguments()[0];
        return null;
    }
}
