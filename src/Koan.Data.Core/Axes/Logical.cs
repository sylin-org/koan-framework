namespace Koan.Data.Core.Axes;

/// <summary>
/// A declarative operation-semantics override (ARCH-0101 §4) authored on the <see cref="Axis"/> builder via
/// <see cref="Axis.OnDelete"/>. <b>Data, not a callback</b> (descriptor-not-callback): it names the field a delete
/// sets and the value to set — the framework owns the chokepoint rewrite. Expands to an
/// <c>OperationOverrideDescriptor</c>.
/// </summary>
/// <param name="Field">The managed field the delete sets — MUST be a <see cref="Axis.Field"/> the same axis declares.</param>
/// <param name="OnDeleteValue">The value to set on <c>Delete</c> (e.g. <c>true</c> for soft-delete).</param>
public readonly record struct LogicalDelete(string Field, object? OnDeleteValue);

/// <summary>
/// Authoring helpers for the operation-semantics override (ARCH-0101 §4). <c>Logical.SetTrue("__deleted")</c> reads as
/// intent — "a delete logically sets <c>__deleted = true</c>" — and produces the declarative <see cref="LogicalDelete"/>.
/// </summary>
public static class Logical
{
    /// <summary>A delete that sets the boolean managed <paramref name="field"/> to <c>true</c> (the soft-delete shape).</summary>
    public static LogicalDelete SetTrue(string field) => new(field, true);

    /// <summary>A delete that sets the managed <paramref name="field"/> to an arbitrary <paramref name="value"/>.</summary>
    public static LogicalDelete Set(string field, object? value) => new(field, value);
}
