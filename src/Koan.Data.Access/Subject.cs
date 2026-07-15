using System;
using System.Collections.Generic;
using Koan.Core.Context;

namespace Koan.Data.Access;

/// <summary>
/// The developer-facing surface for the ambient subject context (SEC-0008), backed by Core's typed logical-flow
/// context. The edge sets it once per request; durable consumers restore it through
/// <see cref="SubjectContextCarrier"/> so guest-triggered work retains the submitting subject.
/// </summary>
public static class Subject
{
    /// <summary>The ambient subject slice, or <c>null</c> when no subject is in scope.</summary>
    public static SubjectContext? Current => KoanContext.Get<SubjectContext>();

    /// <summary>Scope to a <b>constrained</b> subject limited to <paramref name="scopes"/> (a guest) — the default, safe verb.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable Use(string subjectId, IEnumerable<string> scopes)
        => KoanContext.Push(SubjectContext.For(subjectId, scopes));

    /// <summary>
    /// Scope to an <b>unconstrained</b> subject — FULL access to every <c>[AccessScoped]</c> entity (the access axis adds
    /// no scope). Deliberately a distinct, greppable verb (not a no-scopes <see cref="Use"/> overload) so guest god-mode
    /// by omission is loud, like <see cref="System"/>. Use for an operator/admin the tenant axis already isolates.
    /// </summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static IDisposable Unconstrained(string subjectId) => KoanContext.Push(SubjectContext.For(subjectId));

    /// <summary>Enter explicit <b>elevated / system scope</b> — no access constraint, no identity (the loud escape, like <c>Tenant.None()</c>).</summary>
    public static IDisposable System() => KoanContext.Push(SubjectContext.System);
}
