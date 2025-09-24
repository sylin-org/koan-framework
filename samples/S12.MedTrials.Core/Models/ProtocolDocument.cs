using System;
using System.Collections.Generic;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Data.Vector.Abstractions;
using Koan.Mcp;

namespace S12.MedTrials.Models;

[McpEntity(
    Name = "ProtocolDocument",
    Description = "Protocol and regulatory guidance library",
    RequiredScopes = new[] { "clinical:documents" })]
[VectorAdapter("weaviate")]
public sealed class ProtocolDocument : Entity<ProtocolDocument>
{
    [Parent(typeof(TrialSite))]
    public string? TrialSiteId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "Protocol";
    public string? Version { get; set; }
    public string? FileReference { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset EffectiveDate { get; set; } = DateTimeOffset.UtcNow;
    public ProtocolVectorState VectorState { get; set; } = ProtocolVectorState.Pending;
    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastEmbeddedAt { get; set; }
    public List<DocumentDiagnostic> Diagnostics { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}
