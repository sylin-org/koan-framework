using System;

namespace Koan.Web.Extensions;

/// <summary>
/// ARCH-0092 (§B): terse REST exposure — the symmetric peer of <c>[McpEntity]</c>. Annotating an
/// <c>Entity&lt;T&gt;</c> with <c>[RestEntity]</c> auto-registers a full-CRUD
/// <see cref="Koan.Web.Controllers.EntityController{TEntity,TKey}"/> for it over the existing
/// generic-controller machinery (<see cref="GenericControllers.GenericControllers"/>) — no hand-written
/// controller class required.
/// </summary>
/// <remarks>
/// <para>
/// The attribute declares <i>exposure only</i> ("project this entity over REST"). Access lives on the
/// unified authorization floor (ARCH-0092 §D / SEC-0002), never on this attribute — keeping the
/// domain / exposure / access concerns unbundled.
/// </para>
/// <para>
/// <b>Precedence (§B):</b> an explicit, hand-written <c>EntityController&lt;T&gt;</c> subclass for the same
/// entity wins; the terse registration for that entity is skipped (so the two never collide on a route).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [RestEntity]                  // → CRUD at api/order
/// public sealed class Order : Entity&lt;Order&gt; { }
///
/// [RestEntity("api/orders")]    // → CRUD at the explicit route
/// public sealed class Order : Entity&lt;Order&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RestEntityAttribute : Attribute
{
    public RestEntityAttribute()
    {
    }

    public RestEntityAttribute(string route)
    {
        Route = route;
    }

    /// <summary>
    /// Explicit route prefix (e.g. <c>"api/orders"</c>). When null/blank, the route defaults to
    /// <c>api/{kebab-entity-name}</c> (singular, mirroring <c>[McpEntity]</c>'s entity-name-based tool
    /// naming). There is no built-in pluralizer — set this to pluralize.
    /// </summary>
    public string? Route { get; set; }
}
