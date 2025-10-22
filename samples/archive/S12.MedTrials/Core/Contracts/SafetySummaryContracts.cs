using System.Collections.Generic;
using S12.MedTrials.Models;

namespace S12.MedTrials.Contracts;

public sealed class SafetySummaryRequest
{
    public int LookbackDays { get; set; } = 14;
    public string? TrialSiteId { get; set; }
    public AdverseEventSeverity? MinimumSeverity { get; set; }
    public int MaxEvents { get; set; } = 25;
    public string? Model { get; set; }
}

public sealed record SafetySummaryResult(
    string Summary,
    bool Degraded,
    string? Model,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AdverseEventReport> Events);
