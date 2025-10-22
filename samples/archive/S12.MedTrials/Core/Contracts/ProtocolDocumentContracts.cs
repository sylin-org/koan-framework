using System;
using System.Collections.Generic;
using S12.MedTrials.Models;

namespace S12.MedTrials.Contracts;

public sealed class ProtocolDocumentIngestionRequest
{
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "Protocol";
    public string Content { get; set; } = string.Empty;
    public string? TrialSiteId { get; set; }
    public string? Version { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset? EffectiveDate { get; set; }
    public string? SourceUrl { get; set; }
}

public sealed record ProtocolDocumentIngestionResult(
    ProtocolDocument Document,
    bool Vectorized,
    bool Degraded,
    IReadOnlyList<string> Warnings,
    string? Model,
    IReadOnlyList<DocumentDiagnostic> Diagnostics);

public sealed class ProtocolDocumentQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public string? TrialSiteId { get; set; }
    public int TopK { get; set; } = 5;
    public bool IncludeContent { get; set; }
}

public sealed record ProtocolDocumentMatch(
    string DocumentId,
    double Score,
    string? Snippet,
    ProtocolDocument Document);

public sealed record ProtocolDocumentQueryResult(
    IReadOnlyList<ProtocolDocumentMatch> Matches,
    bool Degraded,
    string? Model,
    IReadOnlyList<string> Warnings);
