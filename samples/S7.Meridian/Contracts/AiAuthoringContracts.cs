using System;
using System.Collections.Generic;

namespace Koan.Samples.Meridian.Contracts;

public sealed class SourceTypeAiSuggestRequest
{
    public string SeedText { get; set; } = string.Empty;
    public string? DocumentName { get; set; }
        = null;
    public string? AdditionalContext { get; set; }
        = null;
    public List<string> TargetFields { get; set; } = new();
    public List<string> DesiredTags { get; set; } = new();
    public string? Model { get; set; }
        = null;
}

public sealed class SourceTypeAiSuggestResponse
{
    public SourceTypeDraft Draft { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class SourceTypeDraft
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<string> Descriptors { get; set; } = new();
    public List<string> FilenamePatterns { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<string> MimeTypes { get; set; } = new();
    public int? ExpectedPageCountMin { get; set; }
        = null;
    public int? ExpectedPageCountMax { get; set; }
        = null;
    public Dictionary<string, string> FieldQueries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Instructions { get; set; } = string.Empty;
    public string OutputTemplate { get; set; } = string.Empty;
}

public sealed class AnalysisTypeAiSuggestRequest
{
    public string Goal { get; set; } = string.Empty;
    public string? Audience { get; set; }
        = null;
    public List<string> IncludedSourceTypes { get; set; } = new();
    public string? AdditionalContext { get; set; }
        = null;
    public string? Model { get; set; }
        = null;
}

public sealed class AnalysisTypeAiSuggestResponse
{
    public AnalysisTypeDraft Draft { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class AnalysisTypeDraft
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<string> Descriptors { get; set; } = new();
    public string Instructions { get; set; } = string.Empty;
    public string OutputTemplate { get; set; } = string.Empty;
    public List<string> RequiredSourceTypes { get; set; } = new();
}
