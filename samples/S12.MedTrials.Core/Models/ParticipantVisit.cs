using System;
using System.Collections.Generic;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Mcp;

namespace S12.MedTrials.Models;

[McpEntity(
    Name = "ParticipantVisit",
    Description = "Participant visit schedules with AI proposed adjustments",
    RequiredScopes = new[] { "clinical:operations" })]
public sealed class ParticipantVisit : Entity<ParticipantVisit>
{
    [Parent(typeof(TrialSite))]
    public string TrialSiteId { get; set; } = string.Empty;

    public string ParticipantId { get; set; } = string.Empty;
    public VisitType VisitType { get; set; } = VisitType.Baseline;
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public VisitStatus Status { get; set; } = VisitStatus.Scheduled;
    public string? Cohort { get; set; }
    public string? WindowLabel { get; set; }
    public List<VisitAdjustment> ProposedAdjustments { get; set; } = new();
    public List<VisitDiagnostic> Diagnostics { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
