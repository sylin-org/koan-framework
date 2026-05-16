using System;

namespace S12.MedTrials.Models;

public sealed class VisitAdjustment
{
    public string ProposedBy { get; set; } = "planner";
    public DateTimeOffset ProposedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SuggestedDate { get; set; } = DateTimeOffset.UtcNow;
    public string? VisitId { get; set; }
    public string Summary { get; set; } = "";
    public string? Rationale { get; set; }
    public string Source { get; set; } = "heuristic";
    public string[] Citations { get; set; } = [];
    public bool RequiresApproval { get; set; } = true;
    public string? Model { get; set; }
}

public sealed class VisitDiagnostic
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Details { get; set; }
}
