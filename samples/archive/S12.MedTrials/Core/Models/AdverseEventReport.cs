using System;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Data.Vector.Abstractions;
using Koan.Mcp;

namespace S12.MedTrials.Models;

[McpEntity(
    Name = "AdverseEventReport",
    Description = "Safety incident tracking with audit diagnostics",
    RequiredScopes = new[] { "clinical:safety" })]
[VectorAdapter("weaviate")]
public sealed class AdverseEventReport : Entity<AdverseEventReport>
{
    [Parent(typeof(TrialSite))]
    public string TrialSiteId { get; set; } = "";

    [Parent(typeof(ParticipantVisit))]
    public string? ParticipantVisitId { get; set; }

    public string ParticipantId { get; set; } = "";
    public AdverseEventSeverity Severity { get; set; } = AdverseEventSeverity.Moderate;
    public string Description { get; set; } = "";
    public DateOnly OnsetDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public AdverseEventStatus Status { get; set; } = AdverseEventStatus.Open;
    public string[] SourceDocuments { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string? VectorId { get; set; }
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
