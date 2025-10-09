using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Represents a curated, manual multi-document analysis session.
/// Stores document selection, prompt overrides, and the latest synthesis output.
/// </summary>
[McpEntity(Name = "manual-analysis-sessions", Description = "Curated DocMind manual analysis sessions.")]
public sealed class ManualAnalysisSession : Entity<ManualAnalysisSession>
{
    [Required, MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }
        = null;

    [MaxLength(120)]
    public string? Owner { get; set; }
        = null;

    public ManualAnalysisStatus Status { get; set; }
        = ManualAnalysisStatus.Draft;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }
        = null;

    public DateTimeOffset? LastRunAt { get; set; }
        = null;

    [Parent(typeof(SemanticTypeProfile))]
    public Guid? ProfileId { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public ManualAnalysisPrompt Prompt { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public List<ManualAnalysisDocument> Documents { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public ManualAnalysisSynthesis? LastSynthesis { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public List<ManualAnalysisRun> RunHistory { get; set; }
        = new();
}

public enum ManualAnalysisStatus
{
    Draft = 0,
    Ready = 1,
    Running = 2,
    Completed = 3,
    Archived = 4
}

/// <summary>
/// Manual prompt overrides captured for a session.
/// </summary>
public sealed class ManualAnalysisPrompt
{
    [MaxLength(2000)]
    public string? Instructions { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Variables { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Document selection and annotations for a manual session.
/// </summary>
public sealed class ManualAnalysisDocument
{
    public Guid SourceDocumentId { get; set; }
        = Guid.Empty;

    [MaxLength(255)]
    public string? DisplayName { get; set; }
        = null;

    public bool IncludeInSynthesis { get; set; }
        = true;

    [MaxLength(2000)]
    public string? Notes { get; set; }
        = null;

    public DateTimeOffset AddedAt { get; set; }
        = DateTimeOffset.UtcNow;
}

/// <summary>
/// Captures the most recent synthesis output for a manual session.
/// </summary>
public sealed class ManualAnalysisSynthesis
{
    public DateTimeOffset GeneratedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public double? Confidence { get; set; }
        = null;

    public string? FilledTemplate { get; set; }
        = null;

    public string? ContextSummary { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Metadata { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    [Column(TypeName = "jsonb")]
    public List<ManualAnalysisFinding> Findings { get; set; }
        = new();
}

/// <summary>
/// Individual finding persisted alongside a manual synthesis run.
/// </summary>
public sealed class ManualAnalysisFinding
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public double? Confidence { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public List<string> Sources { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Structured { get; set; }
        = new();
}

/// <summary>
/// Telemetry captured for each manual session execution.
/// </summary>
public sealed class ManualAnalysisRun
{
    public DateTimeOffset ExecutedAt { get; set; }
        = DateTimeOffset.UtcNow;

    [MaxLength(120)]
    public string Model { get; set; } = string.Empty;

    public long? TokensIn { get; set; }
        = null;

    public long? TokensOut { get; set; }
        = null;

    public double? Confidence { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public List<Guid> DocumentIds { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Metadata { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
