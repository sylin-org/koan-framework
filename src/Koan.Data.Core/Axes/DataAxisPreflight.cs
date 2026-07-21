using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The boot-refuses-leaky-axis pre-flight (ARCH-0101 §8) — the fail-fast companion of <see cref="DataAxis.Explain"/>.
/// At boot it sweeps the discovered entity types and, for each that is <b>read-scoped in the boot ambient</b> (an
/// always-on axis — a soft-delete / moderation read predicate, or an equality field with a constant value provider —
/// NOT an ambient-gated axis whose value is null until a scope is entered), checks the routed adapter can actually
/// enforce the isolation. A confirmed mismatch is a <b>leak</b>: reads would silently return cross-scope rows.
///
/// <para><b>Koan posture (delightful by design):</b> in <b>Development</b> the leak is a <i>loud, actionable warning</i>
/// and boot continues (you keep iterating; the runtime fail-closed still throws on a real scoped op) — in
/// <b>Production</b> it <i>refuses boot</i> with the same message (the deploy-time safety net). Clean apps see nothing
/// (off = silent). The reused inspection authority is the facade diagnostic (so the verdict matches a real read).</para>
///
/// <para><b>Scope boundary (deliberate, v1):</b> the pre-flight reports a leak only when an axis is <i>active in the
/// boot ambient</i> (a non-null read fold). An <b>ambient-gated</b> axis (the tenant write-stamp — null at boot) and a
/// <b>write-stamp-only</b> managed field (no read predicate) are NOT boot-refused here; they are enforced by the runtime
/// fail-closed on the first scoped op (the DATA-0106 "off axis is a true no-op" intent — a tenant entity does not
/// read-scope until a tenant is in scope). An <b>unresolvable</b> adapter at boot is conservatively skipped (the runtime
/// backstops it; a configured-but-unconstructable adapter serves no rows), so the boot gate never false-positive-refuses
/// a Production deploy.</para>
/// </summary>
public static class DataAxisPreflight
{
    // Resolved once (open generic) — MakeGenericMethod per boot-scoped entity. Cheap; the pre-flight is not a hot path.
    private static readonly MethodInfo GetScopeDiagnosticsMethod =
        typeof(IDataService).GetMethod(nameof(IDataService.GetScopeDiagnostics))!;

    /// <summary>Run the pre-flight: warn (non-Production) or refuse boot (Production) on a confirmed leak. No-op when no
    /// isolation axis is registered (the byte-identical fast path).</summary>
    public static void Run(IServiceProvider services, IHostEnvironment environment, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        var leaks = DetectLeaks(services);
        if (leaks.Count == 0) return;

        var refuses = environment.IsProduction();
        var message = Format(leaks, refuses);
        if (refuses)
        {
            logger?.LogCritical("{Message}", message);
            throw new DataAxisLeakException(leaks, message);
        }
        // Loud, never lost: log if a logger exists, else fall back to the console (the same channel the bootstrapper
        // uses before logging is up) — a delightful warning is no use if it is silently dropped.
        if (logger is not null) logger.LogWarning("{Message}", message);
        else Console.Error.WriteLine("[Koan] " + message);
    }

    /// <summary>The confirmed boot-time leaks (an always-on read scope on an adapter that cannot enforce it). Empty when
    /// no axis is registered, or every boot-scoped entity routes to an isolating, pushdown-capable adapter.</summary>
    public static IReadOnlyList<DataAxisLeak> DetectLeaks(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Gate: nothing to check unless SOME isolation axis is registered — a managed field (tenancy / classification /
        // soft-delete discriminator, incl. a constant-value equality axis) OR a non-default read contributor (a
        // moderation predicate). No axis ⇒ byte-identical no-op (the data-core off-proof). Cheap volatile + LINQ.
        var contributors = services.GetServices<IReadFilterContributor>().ToArray();
        if (ManagedFieldRegistry.IsEmpty && !contributors.Any(c => c is not ManagedEqualityReadContributor))
            return Array.Empty<DataAxisLeak>();
        if (services.GetService(typeof(IDataService)) is not IDataService dataService) return Array.Empty<DataAxisLeak>();

        var leaks = new List<DataAxisLeak>();
        foreach (var type in EntityTypes())
        {
            // Best-effort per entity: an entity whose fold / pushability cannot be cleanly evaluated at boot (an
            // unresolvable adapter, a contributor referencing a not-yet-registered managed field, a contributor that
            // throws) is SKIPPED — the runtime fail-closed remains the backstop. The pre-flight only ever reports a
            // CONFIRMED, cleanly-evaluable leak, so it never crashes boot on an evaluation hiccup nor false-positives.
            try
            {
                var keyType = DataAxis.KeyTypeOf(type);
                if (keyType is null) continue;
                if (ReadScopeFold.Fold(contributors, type) is not { } fold) continue;   // not read-scoped in the boot ambient

                var diag = ResolveDiagnostics(dataService, type, keyType);
                if (!diag.ScopeAdapterOk)
                    leaks.Add(new DataAxisLeak(type, diag.AdapterName,
                        diag.ScopeAdapterError ?? "the adapter cannot enforce the active read-scope."));
                else if (!diag.IsFullyPushable(fold))
                    leaks.Add(new DataAxisLeak(type, diag.AdapterName,
                        $"the active read-scope predicate cannot be fully pushed down by '{diag.AdapterName}' — an isolation " +
                        "filter must be enforced at the store, never evaluated in memory (a residual fetches cross-scope rows)."));
            }
            catch
            {
                // skip this entity (see the comment above) — never crash boot on a best-effort evaluation
            }
        }
        return leaks;
    }

    private static IAxisScopeDiagnostics ResolveDiagnostics(IDataService dataService, Type entityType, Type keyType)
    {
        var method = GetScopeDiagnosticsMethod.MakeGenericMethod(entityType, keyType);
        try { return (IAxisScopeDiagnostics)method.Invoke(dataService, null)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
    }

    // The discovered entity types from the bootstrapper's curated assembly closure (not a fresh AppDomain scan).
    private static IEnumerable<Type> EntityTypes()
    {
        foreach (var assembly in AssemblyCache.Instance.GetAllAssemblies())
        {
            Type?[] types;
            try { types = assembly.GetTypes(); }
            // A partial-load failure carries the loadable types in ex.Types — keep them rather than skip the WHOLE
            // assembly (which could silently drop a genuinely-leaky entity, fail-opening the gate). Other failures skip.
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            catch { continue; }
            foreach (var t in types)
                if (t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
                    yield return t;
        }
    }

    private static string Format(IReadOnlyList<DataAxisLeak> leaks, bool refuses)
    {
        var sb = new StringBuilder();
        sb.Append("Koan data-axis isolation: ").Append(leaks.Count)
          .Append(leaks.Count == 1 ? " entity is" : " entities are")
          .AppendLine(" read-scoped by an axis their adapter CANNOT enforce — reads would leak across scopes (soft-deleted rows stay visible / cross-scope rows returned).");
        foreach (var leak in leaks)
            sb.Append("  • ").Append(leak.Entity.FullName).Append(" → ").AppendLine(leak.Reason);
        sb.Append(refuses
            ? "Refusing to boot in Production. Route each entity to an isolating, pushdown-capable adapter (sqlite/postgres/sqlserver/mongo), or remove its axis."
            : "Permitted in Development (boot continues; a real scoped op still fails closed) — but this REFUSES boot in Production. Run DataAxis.Explain<T>(sp) for the per-entity RSoP.");
        return sb.ToString();
    }
}
