using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Koan.Data.Access;

/// <summary>
/// The immutable ambient <b>subject context</b> (SEC-0008) — the "who is asking", stored as an exact-type value in
/// Core's logical-flow context. The data-layer access axis reads four ambient states:
/// <list type="bullet">
///   <item>no <see cref="SubjectContext"/> in scope — <b>no subject</b> (an access-scoped read fails closed, per <see cref="AccessOptions"/>);</item>
///   <item><see cref="IsSystem"/> — <b>explicit elevated / control-plane scope</b> (<c>Subject.System()</c>), no access constraint, no identity;</item>
///   <item><see cref="Scopes"/> is <c>null</c> — an <b>unconstrained</b> subject (full access; e.g. an operator the tenant axis already isolates);</item>
///   <item><see cref="Scopes"/> is set — <b>constrained</b> to those access-scope tokens (a guest); empty ⇒ sees nothing.</item>
/// </list>
/// Snapshot-fed: the scope tokens are resolved once at the edge (reading grants) and carried, so the read-path
/// predicate is a pure set-membership test, never a per-query re-query.
/// </summary>
public sealed record SubjectContext
{
    private const char UnitSeparator = '\u001f';

    private SubjectContext() { }

    /// <summary>The subject (person / principal) id; <c>null</c> for the <see cref="System"/> (elevated) scope.</summary>
    public string? Id { get; private init; }

    /// <summary>True for the explicit elevated / control-plane scope (<c>Subject.System()</c>) — no access constraint.</summary>
    public bool IsSystem { get; private init; }

    /// <summary>The access-scope tokens the subject is limited to; <c>null</c> = unconstrained (full). Empty = deny-all.</summary>
    public IReadOnlySet<string>? Scopes { get; private init; }

    /// <summary>True when this subject is limited to <see cref="Scopes"/> (a guest), vs unconstrained / system.</summary>
    public bool IsConstrained => Scopes is not null;

    /// <summary>The shared, explicit elevated scope (no identity, no access constraint) — the <c>Tenant.None()</c> analogue.</summary>
    public static readonly SubjectContext System = new() { IsSystem = true };

    /// <summary>An <b>unconstrained</b> subject (full access). Validates the id at the boundary.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    public static SubjectContext For(string subjectId)
    {
        ValidateSubjectId(subjectId);
        return new SubjectContext { Id = subjectId };
    }

    /// <summary>A <b>constrained</b> subject limited to <paramref name="scopes"/> (a guest); empty ⇒ sees nothing.</summary>
    /// <exception cref="ArgumentException">The id is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException">The scopes enumerable is null.</exception>
    public static SubjectContext For(string subjectId, IEnumerable<string> scopes)
    {
        ValidateSubjectId(subjectId);
        if (scopes is null) throw new ArgumentNullException(nameof(scopes));
        // Always copy to a frozen ordinal set — immutable value, with no caller alias or cast-mutable collection. Empty
        // tokens are dropped; the carrier's unit separator (U+001F) is rejected so the captured form cannot be split
        // into two tokens across the async hop (which would silently BROADEN authorization — SEC-0008 review).
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in scopes)
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (s.IndexOf(UnitSeparator) >= 0)
                throw new ArgumentException("A subject scope token must not contain the unit separator (U+001F).", nameof(scopes));
            set.Add(s);
        }
        return new SubjectContext { Id = subjectId, Scopes = set.ToFrozenSet(StringComparer.Ordinal) };
    }

    private static void ValidateSubjectId(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("A subject id must be a non-empty value.", nameof(subjectId));
        if (subjectId.IndexOf(UnitSeparator) >= 0)
            throw new ArgumentException("A subject id must not contain the unit separator (U+001F).", nameof(subjectId));
    }
}
