using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity.Credentials.StepUp;

/// <summary>
/// SEC-0007 P3-grp4 — a short-lived, single-use resume token for an interrupted (step-up-required) sign-in. The gate
/// mints one and aborts the sign-in (no <c>Session</c>); the factor-challenge endpoint resolves it, verifies the
/// pending factor, then re-issues the sign-in with that factor's <c>amr</c> method added. <c>IAmbientExempt</c> (the
/// global identity plane). The token id is the unguessable handle (GUID v7); it carries no secret material.
/// </summary>
public sealed class StepUpTicket : Entity<StepUpTicket>, IAmbientExempt
{
    /// <summary>The person resuming the interrupted sign-in.</summary>
    public string IdentityId { get; set; } = "";

    /// <summary>The <c>amr</c> methods already proven this round (e.g. <c>["pwd"]</c>).</summary>
    public List<string> Satisfied { get; set; } = new();

    /// <summary>The requirement factors still pending (e.g. <c>["mfa"]</c>).</summary>
    public List<string> Pending { get; set; } = new();

    /// <summary>When the ticket stops being redeemable.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set once the ticket has been consumed — a ticket is single-use.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True when the ticket is unconsumed and not past its expiry at <paramref name="now"/>.</summary>
    public bool IsRedeemable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;
}
