namespace Koan.Identity.Credentials.StepUp;

/// <summary>SEC-0007 P3-grp4 — configuration for the 2-phase step-up sign-in.</summary>
public sealed class StepUpOptions
{
    public const string SectionPath = "Koan:Identity:StepUp";

    /// <summary>The reject-reason prefix the gate emits; the factor-challenge controller parses the ticket id after it.</summary>
    public const string StepUpRejectPrefix = "koan_stepup:";

    /// <summary>How long an interrupted sign-in may be resumed before its ticket expires. Default 5 minutes.</summary>
    public TimeSpan TicketLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
