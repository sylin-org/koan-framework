using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core.Hosting.Registry;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The boot-time engine that turns discovered <see cref="IDataAxis"/> authoring (ARCH-0101 §7) into the exact raw
/// Phase A/B/C seam registrations — so a <c>[DataAxis]</c> and the equivalent hand-written registration produce
/// byte-identical behavior. Invoked once from <c>RegisterKoanDataCoreServices</c> (before the IDataService add, after
/// the built-in equality contributor); discovery is already populated by then (the manifest loader
/// runs in the assembly-closure walk, before the initializer/Register loop).
///
/// <para>The static registries (<see cref="ManagedFieldRegistry"/>, <see cref="StorageNameParticleRegistry"/>,
/// <see cref="OperationOverrideRegistry"/>) dedup by key, so re-entrant / cross-host expansion is idempotent. The
/// DI-enumerable read-filter seams are per-ServiceCollection — the expander runs once per collection (the IDataService
/// guard), so NO static guard is needed for them.</para>
///
/// <para><b>Fail-loud collision detection (ARCH-0101 §8).</b> The within-batch checks name two colliding axes; a
/// <b>cross-source</b> field collision — a <c>[DataAxis]</c> reusing a managed-field name a hand-written module
/// (e.g. <c>Koan.Data.SoftDelete</c>'s <c>__deleted</c>, <c>Koan.Tenancy</c>'s <c>__koan_tenant</c>) already registered
/// — would otherwise be silently first-wins-dropped by the registry's idempotent dedup (a nondeterministic, boot-order
/// dependent half-axis / isolation hole). The expander owns a per-field ledger (<see cref="_fieldOwners"/>) so it can
/// tell <i>its own re-entrant re-expansion</i> (same axis, another host) from a genuine cross-source clash, and throws
/// loudly on the latter. The override plane rides the field (its <c>OnDelete</c> name must equal a declared
/// <c>.Field</c>), and the name-particle plane's axis id is the within-batch dedup key (only the expander registers
/// particles), so both are covered.</para>
/// </summary>
public static class DataAxisExpander
{
    // Cross-host field-ownership ledger: managed-field StorageName → the axis id that first claimed it via this expander.
    // Lets a re-entrant re-expansion (same axis, a second host in one process) be recognized as self (idempotent) while a
    // field present in ManagedFieldRegistry but ABSENT from this ledger is owned by another source (a hand-written
    // registrar) ⇒ a cross-source collision ⇒ fail loud. Static so it persists across hosts; reset in tests.
    private static readonly object _ledgerGate = new();
    private static readonly Dictionary<string, string> _fieldOwners = new(StringComparer.Ordinal);

    /// <summary>Discover every <see cref="IDataAxis"/> implementor and expand it. The production entry point.</summary>
    public static void ExpandDiscovered(IServiceCollection services)
        => Expand(KoanRegistry.GetDiscoveredImplementors(typeof(IDataAxis)), services);

    /// <summary>
    /// Instantiate each axis type, declare it, and expand. Skips a type that is abstract or has no public parameterless
    /// constructor (it cannot be an axis — a generic/DI-constructed type or a test double); a well-formed axis always
    /// has one. Exposed (independent of discovery) so the skip guard is directly testable.
    /// </summary>
    public static void Expand(IEnumerable<Type> axisTypes, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(axisTypes);
        ArgumentNullException.ThrowIfNull(services);
        var builders = new List<Axis>();
        foreach (var type in axisTypes)
        {
            if (type is null || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null) continue;
            var axis = (IDataAxis)Activator.CreateInstance(type)!;
            var builder = new Axis();
            axis.Declare(builder);
            builders.Add(builder);
        }
        ExpandAxes(builders, services);
    }

    /// <summary>
    /// The testable core: validate, batch-collision-check, and register a set of declared <see cref="Axis"/> builders
    /// (independent of discovery). Two passes so a malformed or colliding axis fails loud <b>before</b> any registry
    /// write (never a half-applied batch).
    /// </summary>
    public static void ExpandAxes(IReadOnlyList<Axis> axes, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(services);
        if (axes.Count == 0) return;   // off = structurally absent

        // Pass 1 — validate each axis, then detect WITHIN-BATCH collisions and NAME the offending axes (the static
        // registries silently dedup by key, which is correct for re-entrant Reference=Intent but the WRONG diagnostic
        // for two DISTINCT axes claiming the same key — surface it loudly here instead). Cross-SOURCE collisions (vs a
        // hand-written registrar already in the static registries) are caught in Pass 2 via the field-ownership ledger.
        var seenIds = new Dictionary<string, int>(StringComparer.Ordinal);          // axis id → index
        var seenFields = new Dictionary<string, string>(StringComparer.Ordinal);    // managed field name → axis id
        for (var i = 0; i < axes.Count; i++)
        {
            var axis = axes[i];
            axis.Validate();
            var id = axis.Id!;

            if (!seenIds.TryAdd(id, i))
                throw new InvalidOperationException(
                    $"Two data axes share the logical id '{id}'. Each [DataAxis] must be .Named(...) uniquely.");

            // A managed field is registered only in Shared mode (Container reuses .Field as a particle token).
            if (axis.AxisMode == AxisMode.Shared && axis.FieldName is { } field && seenFields.TryGetValue(field, out var owner))
                throw new InvalidOperationException(
                    $"Data axes '{owner}' and '{id}' both declare the managed field '{field}'. A managed field name must be unique across axes.");
            if (axis.AxisMode == AxisMode.Shared && axis.FieldName is { } f2)
                seenFields[f2] = id;

        }

        // Pass 2 — register the planes. Order within an axis is moot (the registries fold deterministically).
        foreach (var axis in axes)
            Register(axis, services);
    }

    private static void Register(Axis axis, IServiceCollection services)
    {
        var id = axis.Id!;
        var appliesTo = axis.AppliesToPredicate;

        switch (axis.AxisMode)
        {
            case AxisMode.Shared:
                if (axis.FieldName is { } fieldName)
                {
                    // Fail loud on a CROSS-SOURCE field clash (a reserved name a hand-written module already owns) before
                    // the registry silently first-wins-drops it (ARCH-0101 §8). Re-entrant self (same axis, another host)
                    // is recognized via the ledger and proceeds idempotently.
                    ClaimField(fieldName, id);

                    // Smart defaults (ARCH-0101 §7/§8): RowScoped fail-closed, indexed, auto-equality unless a .Reads
                    // predicate replaces it. Cache-key partitioning is automatic (the cache reads ManagedFieldRegistry).
                    ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
                        StorageName: fieldName,
                        ClrType: axis.FieldClrType ?? typeof(string),
                        ValueProvider: axis.FieldValueProvider!,
                        AppliesTo: appliesTo,
                        RequiredCapability: DataCaps.Isolation.RowScoped,
                        Indexed: true,
                        Priority: 0,
                        AutoReadFilter: axis.AutoReadFilter,
                        // ARCH-0102 §3: a field whose value is set by the delete override (soft-delete shape, a null
                        // ambient provider) is operation-sourced; a plain ambient .Field is ambient-stamped. Derived,
                        // never author-typed. (A future axis that is genuinely BOTH declares it explicitly.)
                        Provenance: axis.OnDeleteValue is { } odv && string.Equals(odv.Field, fieldName, StringComparison.Ordinal)
                            ? FieldProvenance.OperationSourced
                            : FieldProvenance.AmbientStamped));
                }

                if (axis.ReadPredicate is { } predicate)
                {
                    // Plain Add (NOT TryAddEnumerable): every axis's contributor is the SAME CLR type, so TryAddEnumerable
                    // (dedup-by-impl-type) would collapse them into one — a silent read-scope hole. Hard-bound to RowScoped
                    // (a hidden-row predicate without isolation is a leak) + cache-excluded (DATA-0106 §5).
                    services.Add(ServiceDescriptor.Singleton<IReadFilterContributor>(
                        new DelegatingReadFilterContributor(id, appliesTo, predicate, DataCaps.Isolation.RowScoped)));
                }

                if (axis.OnDeleteValue is { } od)
                    OperationOverrideRegistry.Register(new OperationOverrideDescriptor(od.Field, od.OnDeleteValue, appliesTo));
                break;

            case AxisMode.Container:
                // The separate-container plane: .Field's value source becomes a leading name particle (anchor untouched).
                StorageNameParticleRegistry.Register(new DelegatingNameParticleContributor(id, appliesTo, axis.FieldValueProvider!));
                break;

            case AxisMode.Database:
                // ARCH-0102 §3 (auto-routing): the .Field value provider is the per-operation SOURCE-KEY provider — register
                // it as a route so AdapterResolver derives the data source from the ambient. Durable carriage is registered
                // independently by the module that owns that context; this Data seam remains persistence-only.
                DatabaseRouteRegistry.Register(new DatabaseRouteDescriptor(id, appliesTo, axis.FieldValueProvider!));
                break;
        }
    }

    // Claim a managed-field name for an axis. Throws on a cross-source clash: the name is already in ManagedFieldRegistry
    // but this expander never claimed it for THIS axis ⇒ a hand-written module (or, defensively, a different axis across
    // hosts) owns it. A re-entrant claim by the same axis is a no-op.
    private static void ClaimField(string fieldName, string axisId)
    {
        lock (_ledgerGate)
        {
            if (_fieldOwners.TryGetValue(fieldName, out var owner))
            {
                if (!string.Equals(owner, axisId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Data axis '{axisId}' declares the managed field '{fieldName}', already claimed by axis '{owner}'. " +
                        "A managed field name must be owned by exactly one axis.");
                return;   // re-entrant self ⇒ idempotent
            }

            if (ManagedFieldRegistry.All.Any(d => string.Equals(d.StorageName, fieldName, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Data axis '{axisId}' declares the managed field '{fieldName}', already registered by another source " +
                    "(a framework module's registrar, e.g. Koan.Data.SoftDelete '__deleted' or Koan.Tenancy '__koan_tenant'). " +
                    "Choose a distinct field name — a managed field must be owned by exactly one source.");

            _fieldOwners[fieldName] = axisId;
        }
    }

    /// <summary>Test-support: clear the cross-host field-ownership ledger (pair with the registries' Reset so a unit
    /// spec sees a clean slate). Production never calls this.</summary>
    public static void ResetForTesting()
    {
        lock (_ledgerGate) _fieldOwners.Clear();
    }
}
