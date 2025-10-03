using System;
using System.Collections.Generic;
using S13.DocMind.Models;

namespace S13.DocMind.Contracts;

public sealed class ManualAnalysisRunRequest
{
    public string? Instructions { get; set; }
        = null;

    public Dictionary<string, string>? Variables { get; set; }
        = null;

    public IReadOnlyList<string>? DocumentIds { get; set; }
        = null;
}

public sealed class ManualAnalysisRunResponse
{
    public required ManualAnalysisSession Session { get; init; }
    public ManualAnalysisSynthesis? Synthesis { get; init; }
    public ManualAnalysisRun? Run { get; init; }
}

public sealed class ManualAnalysisStatsResponse
{
    public int TotalSessions { get; init; }
    public int CompletedSessions { get; init; }
    public int RunningSessions { get; init; }
    public int DraftSessions { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed class ManualAnalysisSummaryResponse
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public ManualAnalysisStatus Status { get; init; }
    public int DocumentCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public string? ProfileId { get; init; }
    public string? PrimaryFinding { get; init; }
    public double? Confidence { get; init; }
}
