namespace Koan.Identity.Credentials.Checkup;

/// <summary>The traffic-light grade of a security-checkup signal — the worst grade across signals wins overall.</summary>
public enum CheckupGrade
{
    /// <summary>Healthy / no action needed.</summary>
    Green = 0,
    /// <summary>A recommended improvement (e.g. add a second factor, set up recovery).</summary>
    Amber = 1,
    /// <summary>A real weakness that should be addressed.</summary>
    Red = 2,
}

/// <summary>
/// One contributed security-posture signal — a category, a traffic-light grade, a human message, and an optional
/// "do this next" action the user can take to improve it.
/// </summary>
public sealed record CheckupSignal(string Category, CheckupGrade Grade, string Message, string? Action = null);

/// <summary>
/// SEC-0007 P3-grp4 — the aggregated <b>Security Checkup</b>: the overall traffic-light (the worst signal) + the
/// per-category signals. The posture synthesis the market leaves to app developers; Koan ships it as substrate over
/// the factor entities + <c>Session</c> + <c>AuditEvent</c> via a contributor pipeline.
/// </summary>
public sealed record SecurityCheckup(string IdentityId, CheckupGrade Overall, IReadOnlyList<CheckupSignal> Signals);
