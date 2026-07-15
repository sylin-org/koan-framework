using System;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The fluent <b>data-axis builder</b> (ARCH-0101 §7) an <see cref="IDataAxis.Declare"/> populates. Purely
/// <b>accumulative</b>: each verb records intent; ALL smart-default derivation, validation
/// (<see cref="Validate"/>), and seam registration happen afterwards in <c>DataAxisExpander</c>, so verb order never
/// changes the result (<c>.Reads().Field()</c> ≡ <c>.Field().Reads()</c>).
///
/// <para>The decisive contract — a <c>[DataAxis]</c> and the equivalent raw-seam registration produce byte-identical
/// behavior:</para>
/// <list type="bullet">
/// <item><see cref="Field"/> (Shared mode) ⇒ a <c>ManagedFieldDescriptor</c> (stamp + serialize + index + cache-key
/// partition); its read scope is the built-in auto-equality fold (no <see cref="Reads"/>) or the explicit
/// <see cref="Reads"/> predicate.</item>
/// <item><see cref="Reads"/> ⇒ a non-equality <c>IReadFilterContributor</c> (RowScoped, excluded from cache); it also
/// turns OFF the field's auto-equality.</item>
/// <item><see cref="OnDelete"/> ⇒ an <c>OperationOverrideDescriptor</c>.</item>
/// <item><see cref="Field"/> (Container mode) ⇒ an <c>IStorageNameParticleContributor</c> (a leading container particle).</item>
/// </list>
/// </summary>
public sealed class Axis
{
    /// <summary>The logical axis id (e.g. <c>"tenant"</c>) — dedup key, boot-report / <c>.Explain</c> label, and the
    /// container particle axis label. Set by <see cref="Named"/>.</summary>
    internal string? Id { get; private set; }

    /// <summary>The composition mode (ARCH-0101 §7). <see cref="AxisMode.Shared"/> by default. Set by <see cref="Mode"/>.</summary>
    internal AxisMode AxisMode { get; private set; } = AxisMode.Shared;

    /// <summary>The per-entity activation predicate (ARCH-0101 §5). Defaults to "all types" until <see cref="AppliesTo"/>.</summary>
    internal Func<Type, bool> AppliesToPredicate { get; private set; } = static _ => true;

    /// <summary>The managed-field / value-source storage name (e.g. <c>"__koan_tenant"</c>), or <c>null</c>.</summary>
    internal string? FieldName { get; private set; }

    /// <summary>Reads the current ambient axis value (the one declarative operand). <c>null</c> until <see cref="Field"/>.</summary>
    internal Func<object?>? FieldValueProvider { get; private set; }

    /// <summary>The managed field's CLR type (defaults to <see cref="string"/> when <see cref="Field"/> omits it).</summary>
    internal Type? FieldClrType { get; private set; }

    /// <summary>The non-equality read predicate (carries the ambient tri-state), or <c>null</c>. Set by <see cref="Reads"/>.</summary>
    internal Func<Type, Filter?>? ReadPredicate { get; private set; }

    /// <summary>The operation-semantics override, or <c>null</c>. Set by <see cref="OnDelete"/>.</summary>
    internal LogicalDelete? OnDeleteValue { get; private set; }

    /// <summary>Whether the field auto-derives an equality read-filter (DATA-0106) — <c>true</c> unless a
    /// <see cref="Reads"/> predicate replaces it. Derived, not stored: order-independent.</summary>
    internal bool AutoReadFilter => ReadPredicate is null;

    /// <summary>Set the logical axis id (required). The dedup key + boot-report / container-particle label.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public Axis Named(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A data axis must be .Named(...) with a non-empty logical id.", nameof(id));
        Id = id;
        return this;
    }

    /// <summary>Set the per-entity activation predicate (ARCH-0101 §5) — broad (<c>t =&gt; !IsHostScoped(t)</c>) or
    /// attribute-gated (<c>t =&gt; t has [SoftDelete]</c>). Must be ambient-INDEPENDENT (a stable type→bool decision);
    /// it is memoized per type on the hot read path.</summary>
    /// <exception cref="ArgumentNullException">The predicate is null.</exception>
    public Axis AppliesTo(Func<Type, bool> predicate)
    {
        AppliesToPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    /// <summary>Set the composition mode (ARCH-0101 §7 — "mode is config"). Default <see cref="AxisMode.Shared"/>.</summary>
    public Axis Mode(AxisMode mode)
    {
        AxisMode = mode;
        return this;
    }

    /// <summary>
    /// Declare the axis value source. In <see cref="AxisMode.Shared"/> it is the invisible managed field — stamped on
    /// write, AND-folded into reads (auto-equality unless <see cref="Reads"/> is also declared), indexed, fail-closed
    /// (RowScoped), and a cache-key partition. In <see cref="AxisMode.Container"/> the same <paramref name="valueProvider"/>
    /// supplies the leading container particle token (<paramref name="storageName"/>/<paramref name="clrType"/> are
    /// then decorative). In <see cref="AxisMode.Database"/> the <paramref name="valueProvider"/> is the per-operation
    /// SOURCE-KEY provider — its value selects the data source the framework routes the operation to (ARCH-0102 §3);
    /// <paramref name="storageName"/>/<paramref name="clrType"/> are decorative (the routing key is the value, not a column).
    /// </summary>
    /// <param name="storageName">The persisted key — must be camel-case-stable (lead with <c>'_'</c> or be all-lowercase);
    /// validated at registration by <c>ManagedFieldRegistry</c>.</param>
    /// <param name="valueProvider">Reads the current ambient axis value; <c>null</c> ⇒ no field/particle (off / host).</param>
    /// <param name="clrType">The field CLR type; defaults to <see cref="string"/>.</param>
    /// <exception cref="ArgumentException">The storage name is null/empty.</exception>
    /// <exception cref="ArgumentNullException">The value provider is null.</exception>
    public Axis Field(string storageName, Func<object?> valueProvider, Type? clrType = null)
    {
        if (string.IsNullOrWhiteSpace(storageName))
            throw new ArgumentException("A data axis .Field(...) requires a non-empty storage name.", nameof(storageName));
        FieldName = storageName;
        FieldValueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
        FieldClrType = clrType;
        return this;
    }

    /// <summary>
    /// Supply a <b>non-equality</b> read-visibility predicate (DATA-0106) — the moderation / soft-delete shape
    /// (<c>visibility IN viewer.clearances</c>, hide-deleted). The predicate MUST carry the ambient tri-state (it is
    /// read live on each query; the type-applicability check is added by the framework). Declaring <see cref="Reads"/>
    /// turns OFF the field's auto-equality and hard-binds the axis to RowScoped + cache-exclusion (a hidden-row predicate
    /// without isolation is a leak — there is no opt-out). For an EQUALITY axis use <see cref="Field"/> alone (its
    /// auto-equality is the memoized, cacheable canon); reach for <see cref="Reads"/> only for a genuine non-equality scope.
    /// </summary>
    /// <exception cref="ArgumentNullException">The predicate is null.</exception>
    public Axis Reads(Func<Type, Filter?> predicate)
    {
        ReadPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    /// <summary>Declare an operation-semantics override (ARCH-0101 §4) — e.g. <c>OnDelete(Logical.SetTrue("__deleted"))</c>.
    /// The field MUST be a <see cref="Field"/> the same axis declares in <see cref="AxisMode.Shared"/> (enforced by
    /// <see cref="Validate"/>).</summary>
    public Axis OnDelete(LogicalDelete op)
    {
        OnDeleteValue = op;
        return this;
    }

    /// <summary>
    /// Validate the accumulated declaration (ARCH-0101 §8 — fail loud at boot, never ship a half-axis). Run by the
    /// expander before any registry write. Enforces: a logical id; at least one plane; and the mode-specific shape —
    /// Shared <c>OnDelete</c> requires the matching <c>Field</c>; Container requires a <c>Field</c> and forbids
    /// <c>Reads</c>/<c>OnDelete</c> (the container IS the isolation); Database requires <c>Field</c> (the source-key
    /// provider) and forbids <c>Reads</c>/<c>OnDelete</c> (a separate data source is the
    /// isolation — ARCH-0102 §3).
    /// </summary>
    /// <exception cref="InvalidOperationException">The declaration is malformed.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidOperationException("A data axis must be .Named(...) with a non-empty logical id.");

        var hasPlane = FieldName is not null || ReadPredicate is not null || OnDeleteValue is not null;
        if (!hasPlane)
            throw new InvalidOperationException(
                $"Data axis '{Id}' declares no plane — call at least one of .Field / .Reads / .OnDelete.");

        switch (AxisMode)
        {
            case AxisMode.Shared:
                if (OnDeleteValue is { } od)
                {
                    if (!string.Equals(od.Field, FieldName, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Data axis '{Id}' declares .OnDelete(\"{od.Field}\") but no matching .Field(\"{od.Field}\"). " +
                            "An operation override stamps a managed field; declare .Field(<name>) for the same name.");
                    // The override value is written into the managed field, so it must match the field's CLR type — else
                    // a mistyped value (e.g. a string into a bool __deleted) is silently serialized AND the hide-deleted
                    // predicate never matches it, leaving the "deleted" row visible. Fail loud at boot (ARCH-0101 §8).
                    var fieldType = FieldClrType ?? typeof(string);
                    if (od.OnDeleteValue is not null && !fieldType.IsInstanceOfType(od.OnDeleteValue))
                        throw new InvalidOperationException(
                            $"Data axis '{Id}' .OnDelete sets '{od.Field}' to a {od.OnDeleteValue.GetType().Name} value, but the " +
                            $"field is declared {fieldType.Name}. The override value must be assignable to the field's CLR type.");
                }
                break;

            case AxisMode.Container:
                if (FieldName is null)
                    throw new InvalidOperationException(
                        $"Container-mode axis '{Id}' requires a .Field(...) as the container-particle value source.");
                // The container token is a string identifier (the anchor is wrapped as `<token>-anchor`); a non-string
                // CLR type collapses to ToString() with no parity to the Shared-mode serialized value and risks a lossy
                // value merging two scopes into one container. Require a string token (convert in the value provider).
                if (FieldClrType is not null && FieldClrType != typeof(string))
                    throw new InvalidOperationException(
                        $"Container-mode axis '{Id}' .Field must produce a string container token (declared CLR type " +
                        $"'{FieldClrType.Name}' is not supported) — convert the value to a string in the value provider.");
                if (ReadPredicate is not null)
                    throw new InvalidOperationException(
                        $"Container-mode axis '{Id}' cannot declare .Reads — a separate container IS the isolation (no read-filter plane).");
                if (OnDeleteValue is not null)
                    throw new InvalidOperationException(
                        $"Container-mode axis '{Id}' cannot declare .OnDelete — there is no managed field to stamp in Container mode.");
                break;

            case AxisMode.Database:
                // ARCH-0102 §3 (auto-routing): in Database mode the .Field value provider is the per-operation SOURCE-KEY
                // provider — its value selects the data source the framework routes to (DataAxisExpander registers it as a
                // DatabaseRouteDescriptor; AdapterResolver derives the source from it). Durable context is a separate,
                // module-owned Core responsibility; Data's axis DSL owns only persistence routing.
                if (FieldName is null)
                    throw new InvalidOperationException(
                        $"Database-mode axis '{Id}' must declare .Field(...) — in Database mode the .Field value provider is the " +
                        "per-operation SOURCE-KEY provider (its value selects the data source the framework routes to). (ARCH-0102 §3)");
                if (ReadPredicate is not null || OnDeleteValue is not null)
                    throw new InvalidOperationException(
                        $"Database-mode axis '{Id}' cannot declare .Reads/.OnDelete — a separate data source IS the isolation; " +
                        "there is no shared-store read-filter or operation-override plane in Database mode (declare .Field only).");
                break;
        }
    }
}
