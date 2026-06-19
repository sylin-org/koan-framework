using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Web.Controllers;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Extensions.Tests;

// ARCH-0092 Phase 2 — terse [RestEntity] entities + one explicit-controller entity for the precedence test.
// Short [StorageName]s keep the in-memory store key tidy (the fully-qualified type name carries dots).

/// <summary>Terse default — CRUD auto-exposed at <c>api/trinket</c> (kebab of the type name).</summary>
[RestEntity]
[StorageName("rest_trinkets")]
public sealed class Trinket : Entity<Trinket>
{
    public string Name { get; set; } = "";
}

/// <summary>Terse with an explicit route override — CRUD at <c>api/gizmos</c>, not <c>api/gizmo</c>.</summary>
[RestEntity("api/gizmos")]
[StorageName("rest_gizmos")]
public sealed class Gizmo : Entity<Gizmo>
{
    public string Label { get; set; } = "";
}

/// <summary>
/// Precedence: carries <c>[RestEntity]</c> AND a hand-written <see cref="CogController"/>. The explicit
/// controller must win — <c>api/cogs</c> is live and the terse <c>api/cog</c> is never registered.
/// </summary>
[RestEntity]
[StorageName("rest_cogs")]
public sealed class Cog : Entity<Cog>
{
    public string Teeth { get; set; } = "";
}

[ApiController]
[Route("api/cogs")]
public sealed class CogController : EntityController<Cog>
{
}
