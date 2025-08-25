namespace Sora.Web.Hooks;

/// <summary>
/// Authorization decision result.
/// </summary>
public abstract record AuthorizeDecision
{
    public sealed record Allow() : AuthorizeDecision;
    public sealed record Forbid(string? Reason = null) : AuthorizeDecision;
    public sealed record Challenge() : AuthorizeDecision;
    public static Allow Allowed() => new();
    public static Forbid Forbidden(string? reason = null) => new(reason);
    public static Challenge Challenged() => new();
}