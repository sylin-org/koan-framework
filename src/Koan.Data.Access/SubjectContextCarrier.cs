using System;
using System.Linq;
using Koan.Data.Core;

namespace Koan.Data.Access;

/// <summary>
/// SEC-0008 / ARCH-0100: carries the ambient <see cref="SubjectContext"/> across a durable async-hop, so a background
/// job (later: a message) runs under the subject that submitted it. This is what makes a <b>guest-triggered job
/// inherently access-scoped</b> — the web-layer access hook could never reach a job. The captured string is
/// <b>versioned</b> so a future format change fails closed at restore (dead-letter) rather than mis-restoring into a
/// wrong/ghost subject. Capture reads only the explicit ambient slice: an unset scope captures nothing.
/// </summary>
public sealed class SubjectContextCarrier : IAmbientSliceCarrier
{
    private const string SystemToken = "v1:system";
    private const string IdPrefix = "v1:id:";          // unconstrained subject: "v1:id:<id>"
    private const string ScopedPrefix = "v1:scoped:";  // constrained: "v1:scoped:<id><US><scope><US><scope>..."
    private const char Unit = '\u001f';                // unit separator (a control char; scope tokens never contain it)

    public string AxisKey => "koan:subject";

    public string? Capture()
    {
        var s = Subject.Current;
        if (s is null) return null;                     // unset → nothing to carry
        if (s.IsSystem) return SystemToken;             // explicit elevated scope
        if (!s.IsConstrained) return IdPrefix + s.Id;   // unconstrained subject
        return ScopedPrefix + s.Id + Unit + string.Join(Unit, s.Scopes!);  // constrained (may be empty → trailing US)
    }

    public IDisposable Restore(string captured)
    {
        if (captured == SystemToken) return Subject.System();

        if (captured.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            var id = captured.Substring(IdPrefix.Length);
            if (id.Length == 0)
                throw new InvalidOperationException("SubjectContextCarrier: the captured subject id is empty — refusing to restore.");
            return Subject.Unconstrained(id);
        }

        if (captured.StartsWith(ScopedPrefix, StringComparison.Ordinal))
        {
            var parts = captured.Substring(ScopedPrefix.Length).Split(Unit);
            var id = parts[0];
            if (id.Length == 0)
                throw new InvalidOperationException("SubjectContextCarrier: the captured subject id is empty — refusing to restore.");
            // parts[1..] are the scope tokens; drop any empties (a trailing US from an empty scope set → constrained-to-nothing).
            return Subject.Use(id, parts.Skip(1).Where(static p => p.Length > 0));
        }

        // Unknown/future format — fail closed (named) so the orchestrator dead-letters rather than mis-restoring.
        throw new InvalidOperationException(
            $"SubjectContextCarrier: cannot restore subject from an unknown captured format '{captured}' (expected version 'v1').");
    }

    // Explicitly clear the ambient subject for this scope — so a job submitted with no subject never inherits the
    // worker/inline-drain thread's subject. Subject.Current becomes null (unset); an access-scoped read then fails
    // closed (or no-ops per AccessOptions), never leaking the carrier thread's subject.
    public IDisposable Suppress() => EntityContext.WithSlice<SubjectContext>(null);
}
