using Koan.Core.Capabilities;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// A declarative descriptor for an <b>invisible, framework-managed field</b> — a value persisted with every
/// record of an applicable entity, scoped from an ambient axis, used to isolate reads/writes — that is
/// <b>not</b> a POCO property (DATA-0105 §0 contributor pattern; see
/// <c>docs/architecture/tenancy-managed-field-design.md</c>). A cross-cutting module (e.g. <c>Koan.Tenancy</c>)
/// registers one of these at boot; the data core honours it generically and never names the axis.
///
/// This is <b>data, not behaviour</b>: the one declared per-axis operand is <see cref="ValueProvider"/> (read
/// once, at the chokepoint). The framework owns every applicator (the serialize injection, the read predicate,
/// the write verify), so a descriptor can never read ambient per-op.
/// </summary>
/// <param name="StorageName">
/// The literal persisted key (e.g. <c>"__koan_tenant"</c>). MUST be a fixed point of camel-case naming (lead
/// with <c>'_'</c> or be all-lowercase) so the write literal and the read literal stay identical on adapters
/// that camel-case filter leaves (SqlServer). Validated at <see cref="ManagedFieldRegistry.Register"/>.
/// </param>
/// <param name="ClrType">The field's CLR type (e.g. <see cref="string"/> for a tenant id).</param>
/// <param name="ValueProvider">Reads the current ambient value for this axis; <c>null</c> ⇒ no field, no predicate.</param>
/// <param name="AppliesTo">Which entity types carry this field (e.g. <c>t =&gt; !IsHostScoped(t)</c>).</param>
/// <param name="RequiredCapability">
/// The adapter capability the field requires (ARCH-0084). When set, an applicable entity on an adapter that
/// does not announce the token — or cannot push a scalar equality on the field — <b>fails closed</b>.
/// </param>
/// <param name="Indexed">Promote to an indexed computed/expression column where the adapter supports it (Schema stage).</param>
/// <param name="Priority">
/// The stable, explicit apply/inject order (lower runs earlier) — the DATA-0105 §3 "total, stable, explicit-priority
/// order frozen at discovery". With a single managed field (the tenant discriminator) order is moot; the field exists
/// so a future second managed field composes deterministically. <see cref="ManagedFieldRegistry.ForType"/> orders by
/// it (stably; ties keep registration order).
/// </param>
/// <param name="AutoReadFilter">
/// Whether the built-in equality read-filter contributor (DATA-0106) derives a scalar
/// <c>Filter.Eq(StorageName, ValueProvider())</c> for this field on every read. <c>true</c> (default) preserves every
/// existing Data-local equality filtering exactly. Cross-pillar caching requires the capability to contribute a
/// Core hard-segmentation dimension; an applicable Data-only managed field is cache-excluded rather than silently
/// omitted from cache identity. <c>false</c> means "stamp + serialize + index me, but I
/// supply my own read predicate via an <c>IReadFilterContributor</c>" — for a <b>non-equality</b> row-visibility axis
/// (e.g. moderation: <c>visibility IN viewer.clearances</c>). A <c>false</c> field still writes its column and still
/// fails closed, but contributes no auto-equality (which would wrongly conjoin) and <b>excludes its entity from the
/// cache</b> (an id-keyed cache namespace is equality-by-construction; a viewer-context predicate cannot be a cache key).
/// </param>
/// <param name="Provenance">
/// ARCH-0102 §3 — where this field's value comes from (<see cref="FieldProvenance"/>), which decides the store-aware
/// push. <see cref="FieldProvenance.AmbientStamped"/> (default — the tenant / moderation shape) is materialised in
/// every store and its predicate is enforceable everywhere; <see cref="FieldProvenance.OperationSourced"/> (soft-delete's
/// <c>__deleted</c>, set only on delete) is materialised only where the operation ran, so a secondary store can't enforce
/// it. Derived from the declared shape, not author-typed (ADR Addendum II).
/// </param>
public sealed record ManagedFieldDescriptor(
    string StorageName,
    Type ClrType,
    Func<object?> ValueProvider,
    Func<Type, bool> AppliesTo,
    Capability? RequiredCapability = null,
    bool Indexed = false,
    int Priority = 0,
    bool AutoReadFilter = true,
    FieldProvenance Provenance = FieldProvenance.AmbientStamped);
