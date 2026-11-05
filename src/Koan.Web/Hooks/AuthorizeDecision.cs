using Koan.Data.Core.Commands;

namespace Koan.Web.Hooks;

/// <summary>
/// Authorization decision result.
/// </summary>
public abstract record AuthorizeDecision
{
    /// <summary>The caller may go on. A route's gate puts <paramref name="Value"/>, what the provider found in judging, on the request as a
    /// feature of its own type, so the action reads it there and nothing is resolved twice (ARCH-0146).</summary>
    public sealed record Allow(object? Value = null) : AuthorizeDecision;
    public sealed record Forbid(string? Reason = null) : AuthorizeDecision;
    public sealed record Challenge() : AuthorizeDecision;

    /// <summary>What this caller addressed does not exist for them: a custom verb answers as it does for what is missing (ARCH-0141).</summary>
    public sealed record Absent() : AuthorizeDecision;

    /// <summary>The caller may not, for this <paramref name="Reason"/>: a route answers it as a command's refusal is answered, with its
    /// sentence and its word (ARCH-0146).</summary>
    public sealed record Refuse(Reason Reason) : AuthorizeDecision;

    public static Allow Allowed(object? value = null) => new(value);
    public static Forbid Forbidden(string? reason = null) => new(reason);
    public static Challenge Challenged() => new();
    public static Absent Hidden() => new();
    public static Refuse Refused(Reason reason) => new(reason ?? throw new ArgumentNullException(nameof(reason)));
}
