using System;
using System.Collections.Generic;
using S12.MedTrials.Models;

namespace S12.MedTrials.Contracts;

public sealed class VisitPlanningRequest
{
    public string TrialSiteId { get; set; } = string.Empty;
    public string[] ParticipantIds { get; set; } = Array.Empty<string>();
    public DateTimeOffset? StartWindow { get; set; }
    public DateTimeOffset? EndWindow { get; set; }
    public int MaxVisitsPerDay { get; set; } = 12;
    public bool AllowWeekendVisits { get; set; } = false;
    public string? Model { get; set; }
    public string? Notes { get; set; }
}

public sealed record VisitPlanningResult(
    IReadOnlyList<VisitAdjustment> Adjustments,
    bool Degraded,
    string? Narrative,
    string? Model,
    IReadOnlyList<VisitDiagnostic> Diagnostics);
