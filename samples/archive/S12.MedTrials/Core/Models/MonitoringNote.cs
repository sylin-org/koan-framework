using System;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Mcp;

namespace S12.MedTrials.Models;

[McpEntity(
    Name = "MonitoringNote",
    Description = "CRA and QA monitoring notes",
    RequiredScopes = new[] { "clinical:operations" })]
public sealed class MonitoringNote : Entity<MonitoringNote>
{
    [Parent(typeof(TrialSite))]
    public string TrialSiteId { get; set; } = "";

    [Parent(typeof(ParticipantVisit))]
    public string? ParticipantVisitId { get; set; }

    public string NoteType { get; set; } = "General";
    public string Summary { get; set; } = "";
    public bool FollowUpRequired { get; set; }
    public string EnteredBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string[] Tags { get; set; } = [];
}
