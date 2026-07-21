using System;
using System.Collections.Generic;

namespace Koan.Web.Authorization;

/// <summary>
/// ARCH-0092 (§D) — the <i>one</i> access-floor primitive ASP.NET cannot express: an OAuth-scope requirement.
/// Declared on an entity, it gates <b>every</b> surface that exposes that entity (REST + MCP), evaluated by the
/// unified <see cref="IAuthorize"/> seam — so the requirement is honored identically everywhere, or not declared
/// (the honesty invariant). Everything else uses standard <c>[Authorize]</c> / <c>[AllowAnonymous]</c>.
/// </summary>
/// <remarks>
/// Multiple scopes are AND-combined (all required). Stack the attribute or pass several names:
/// <c>[RequireScope("orders:read", "orders:write")]</c> ≡ <c>[RequireScope("orders:read")] [RequireScope("orders:write")]</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireScopeAttribute : Attribute
{
    public RequireScopeAttribute(params string[] scopes)
    {
        Scopes = scopes ?? Array.Empty<string>();
    }

    /// <summary>The OAuth scopes the caller must hold (all of them).</summary>
    public IReadOnlyList<string> Scopes { get; }
}
