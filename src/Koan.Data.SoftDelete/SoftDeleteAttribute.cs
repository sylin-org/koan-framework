namespace Koan.Data.SoftDelete;

/// <summary>
/// Opts an entity into <b>soft-delete</b> (ARCH-0101 §4/§5 — attribute-gated activation). On a <c>[SoftDelete]</c>
/// entity, <c>Delete</c> sets an invisible <c>__deleted</c> field instead of physically removing the row, reads hide
/// deleted rows, and the escape verbs <c>.HardDelete()</c> / <c>.Restore()</c> / <c>T.WithDeleted()</c> apply. No
/// property is added to the entity — the discriminator is a framework-managed field. Reference = Intent: referencing
/// <c>Koan.Data.SoftDelete</c> wires the contributors; this attribute is the per-entity opt-in.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SoftDeleteAttribute : Attribute;
