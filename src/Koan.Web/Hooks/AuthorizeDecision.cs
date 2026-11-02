namespace Koan.Web.Hooks;

/// <summary>
/// Authorization decision result.
/// </summary>
public abstract record AuthorizeDecision
{
    public sealed record Allow() : AuthorizeDecision;
    public sealed record Forbid(string? Reason = null) : AuthorizeDecision;
    public sealed record Challenge() : AuthorizeDecision;

    /// <summary>What this caller addressed does not exist for them: a custom verb answers as it does for what is missing (ARCH-0141).</summary>
    public sealed record Absent() : AuthorizeDecision;
    public static Allow Allowed() => new();
    public static Forbid Forbidden(string? reason = null) => new(reason);
    public static Challenge Challenged() => new();
    public static Absent Hidden() => new();
}