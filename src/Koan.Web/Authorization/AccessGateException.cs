using System;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 — a malformed <c>[Access]</c> declaration, raised at boot (or first lazy compile) so a typo can never
/// reach production as a silently-open or silently-denied gate. The boot registrar aggregates every offending
/// entity into one of these so a developer sees them all at once.
/// </summary>
public sealed class AccessGateException : Exception
{
    public AccessGateException(string message) : base(message) { }
}
