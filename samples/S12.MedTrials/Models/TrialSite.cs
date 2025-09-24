using System;
using System.Collections.Generic;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S12.MedTrials.Models;

[McpEntity(
    Name = "TrialSite",
    Description = "Clinical trial site operations surface",
    RequiredScopes = new[] { "clinical:operations" })]
public sealed class TrialSite : Entity<TrialSite>
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string PrincipalInvestigator { get; set; } = string.Empty;
    public int EnrollmentTarget { get; set; }
    public int CurrentEnrollment { get; set; }
    public string RegulatoryStatus { get; set; } = "Pending";
    public string? Phase { get; set; }
    public string? VectorId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
}
