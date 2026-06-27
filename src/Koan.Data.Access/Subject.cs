using System;
using System.Collections.Generic;
using Koan.Data.Core;

namespace Koan.Data.Access;

/// <summary>
/// The developer-facing surface for the ambient subject slice (SEC-0008) — the typed front door over the data core's
/// generic ambient carrier (<c>EntityContext.WithSlice</c>/<c>GetSlice</c>, ARCH-0097), mirroring <c>Tenant</c>. The
/// edge (web/MCP auth middleware) sets it per request; jobs/messages carry it via <see cref="SubjectContextCarrier"/>
/// (ARCH-0100) so a guest-triggered job runs under the submitting subject. Other ambient dimensions carry over.
/// </summary>
public static class Subject
{
    /// <summary>The ambient subject slice, or <c>null</c> when no subject is in scope.</summary>
    public static SubjectContext? Current => EntityContext.GetSlice<SubjectContext>();

    /// <summary>Scope to a <b>constrained</b> subject limited to <paramref name="scopes"/> (a guest) — the default, safe verb.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable Use(string subjectId, IEnumerable<string> scopes)
        => EntityContext.WithSlice(SubjectContext.For(subjectId, scopes));

    /// <summary>
    /// Scope to an <b>unconstrained</b> subject — FULL access to every <c>[AccessScoped]</c> entity (the access axis adds
    /// no scope). Deliberately a distinct, greppable verb (not a no-scopes <see cref="Use"/> overload) so guest god-mode
    /// by omission is loud, like <see cref="System"/>. Use for an operator/admin the tenant axis already isolates.
    /// </summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable Unconstrained(string subjectId) => EntityContext.WithSlice(SubjectContext.For(subjectId));

    /// <summary>Enter explicit <b>elevated / system scope</b> — no access constraint, no identity (the loud escape, like <c>Tenant.None()</c>).</summary>
    public static IDisposable System() => EntityContext.WithSlice(SubjectContext.System);
}
