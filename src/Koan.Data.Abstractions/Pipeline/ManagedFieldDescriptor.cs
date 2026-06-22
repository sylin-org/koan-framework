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
public sealed record ManagedFieldDescriptor(
    string StorageName,
    Type ClrType,
    Func<object?> ValueProvider,
    Func<Type, bool> AppliesTo,
    Capability? RequiredCapability = null,
    bool Indexed = false);
