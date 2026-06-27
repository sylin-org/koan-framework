using System;

namespace Koan.Data.Access;

/// <summary>
/// SEC-0008 — opts an entity into <b>data-layer access scoping</b> (ARCH-0101 §5 attribute-gated activation). On an
/// <c>[AccessScoped]</c> entity, every read (Query/Count and the key-op IDOR lowering) is narrowed to the ambient
/// subject's matching scope tokens, and fails closed when no subject is in scope (per <see cref="AccessOptions"/>).
/// The narrowing is a <b>pushable</b> equality-set filter: <c>Field IN (the subject's scopes that start with
/// ScopePrefix, prefix stripped)</c>. No property is added to the entity. Reference = Intent: referencing
/// <c>Koan.Data.Access</c> wires the axis; this attribute is the per-entity opt-in.
/// <para>
/// Do <b>NOT</b> place this on grant / control-plane tables — they must stay readable to build the subject's scope
/// snapshot. Leaving them un-opted IS the recursion guard (the axis never fires on them), the access analogue of a
/// control-plane row being <c>[HostScoped]</c> for tenancy.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AccessScopedAttribute : Attribute
{
    /// <param name="field">The persisted, pushable entity field the read scope filters (e.g. <c>"eventId"</c>).</param>
    /// <param name="scopePrefix">The subject scope-token prefix this entity scopes by (e.g. <c>"event:"</c>).</param>
    public AccessScopedAttribute(string field, string scopePrefix)
    {
        Field = field;
        ScopePrefix = scopePrefix;
    }

    /// <summary>The entity field the read scope filters (e.g. <c>"eventId"</c>). Must be a persisted, pushable field.</summary>
    public string Field { get; }

    /// <summary>The subject scope-token prefix this entity scopes by (e.g. <c>"event:"</c>). The axis takes the
    /// subject's scopes starting with this prefix, strips it, and builds <c>Field IN (those values)</c>.</summary>
    public string ScopePrefix { get; }
}
