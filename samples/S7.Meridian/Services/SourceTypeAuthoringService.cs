using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Samples.Meridian.Contracts;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

public interface ISourceTypeAuthoringService
{
    Task<SourceTypeAiSuggestResponse> SuggestAsync(SourceTypeAiSuggestRequest request, CancellationToken ct);
}

public sealed class SourceTypeAuthoringService : ISourceTypeAuthoringService
{
    private const int MaxListItems = 16;
    private const int MaxFieldQueryLength = 256;

    private readonly MeridianOptions _options;
    private readonly IAiAssistAuditor _auditor;
    private readonly ILogger<SourceTypeAuthoringService> _logger;

    public SourceTypeAuthoringService(
        IOptions<MeridianOptions> options,
        IAiAssistAuditor auditor,
        ILogger<SourceTypeAuthoringService> logger)
    {
        _options = options.Value;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task<SourceTypeAiSuggestResponse> SuggestAsync(SourceTypeAiSuggestRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SeedText))
        {
            throw new ArgumentException("SeedText is required.", nameof(request));
        }

        var prompt = BuildPrompt(request);
        var model = request.Model ?? _options.Extraction.Model ?? "granite3.3:8b";
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = model,
            Temperature = 0.15,
            MaxTokens = 900
        };

        string rawResponse;
        try
        {
            rawResponse = await Ai.Chat(chatOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI assist failed for source type draft generation.");
            throw;
        }

        var warnings = new List<string>();
        var draft = ParseDraft(rawResponse, warnings);
        SanitizeDraft(draft, warnings);

        var requestSummary = BuildRequestSummary(request);
        var responseSummary = BuildResponseSummary(draft);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["warnings"] = warnings.Count.ToString(),
            ["documentName"] = request.DocumentName ?? string.Empty
        };

        await _auditor.RecordAsync(
            "SourceType",
            draft.Name,
            requestSummary,
            responseSummary,
            chatOptions.Model,
            metadata,
            ct).ConfigureAwait(false);

        return new SourceTypeAiSuggestResponse
        {
            Draft = draft,
            Warnings = warnings
        };
    }

    private static string BuildPrompt(SourceTypeAiSuggestRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are assisting in defining a document source type for an ingestion pipeline.");
        builder.AppendLine("Return ONLY valid JSON with the following schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"name\": \"string\",");
        builder.AppendLine("  \"description\": \"string\",");
        builder.AppendLine("  \"tags\": [\"string\"],");
        builder.AppendLine("  \"descriptors\": [\"string\"],");
        builder.AppendLine("  \"filenamePatterns\": [\"regex\"],");
        builder.AppendLine("  \"keywords\": [\"string\"],");
        builder.AppendLine("  \"mimeTypes\": [\"string\"],");
        builder.AppendLine("  \"expectedPageCount\": { \"min\": number|null, \"max\": number|null },");
        builder.AppendLine("  \"fieldQueries\": { \"$.jsonPath\": \"search query\" },");
        builder.AppendLine("  \"instructions\": \"string\",");
        builder.AppendLine("  \"outputTemplate\": \"string\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("Seed document summary:");
        builder.AppendLine(request.SeedText.Trim());

        if (!string.IsNullOrWhiteSpace(request.DocumentName))
        {
            builder.AppendLine().Append("Document name: ").AppendLine(request.DocumentName.Trim());
        }

        if (request.TargetFields.Count > 0)
        {
            builder.AppendLine().AppendLine("Fields of interest:");
            foreach (var field in request.TargetFields)
            {
                builder.Append("- ").AppendLine(field.Trim());
            }
        }

        if (request.DesiredTags.Count > 0)
        {
            builder.AppendLine().AppendLine("Tags that would be helpful:");
            foreach (var tag in request.DesiredTags)
            {
                builder.Append("- ").AppendLine(tag.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(request.AdditionalContext))
        {
            builder.AppendLine().Append("Additional context: ").AppendLine(request.AdditionalContext.Trim());
        }

        builder.AppendLine("Output must be concise, without explanation text.");
        return builder.ToString();
    }

    private static SourceTypeDraft ParseDraft(string rawResponse, List<string> warnings)
    {
        try
        {
            var json = JObject.Parse(rawResponse);
            var draft = new SourceTypeDraft
            {
                Name = json["name"]?.Value<string>()?.Trim() ?? string.Empty,
                Description = json["description"]?.Value<string>()?.Trim() ?? string.Empty,
                Instructions = json["instructions"]?.Value<string>()?.Trim() ?? string.Empty,
                OutputTemplate = json["outputTemplate"]?.Value<string>()?.Trim() ?? string.Empty
            };

            draft.Tags = ExtractStringArray(json["tags"]);
            draft.Descriptors = ExtractStringArray(json["descriptors"]);
            draft.FilenamePatterns = ExtractStringArray(json["filenamePatterns"]);
            draft.Keywords = ExtractStringArray(json["keywords"]);
            draft.MimeTypes = ExtractStringArray(json["mimeTypes"]);

            var expected = json["expectedPageCount"];
            if (expected?["min"]?.Type is JTokenType.Integer or JTokenType.Float)
            {
                draft.ExpectedPageCountMin = expected["min"]!.Value<int>();
            }

            if (expected?["max"]?.Type is JTokenType.Integer or JTokenType.Float)
            {
                draft.ExpectedPageCountMax = expected["max"]!.Value<int>();
            }

            var queries = json["fieldQueries"] as JObject;
            if (queries is not null)
            {
                foreach (var prop in queries.Properties())
                {
                    var key = prop.Name.Trim();
                    var value = prop.Value.Value<string>()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(key) && !draft.FieldQueries.ContainsKey(key))
                    {
                        draft.FieldQueries[key] = value;
                    }
                }
            }

            return draft;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse AI response: {ex.Message}");
            return new SourceTypeDraft
            {
                Instructions = string.Empty,
                OutputTemplate = string.Empty
            };
        }
    }

    private static List<string> ExtractStringArray(JToken? token)
    {
        if (token is not JArray array)
        {
            return new List<string>();
        }

        return array
            .Values<string?>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();
    }

    private static void SanitizeDraft(SourceTypeDraft draft, List<string> warnings)
    {
        draft.Name = draft.Name.Truncate(128);
        draft.Description = draft.Description.Truncate(512);
        draft.Instructions = draft.Instructions.Truncate(2000);
        draft.OutputTemplate = draft.OutputTemplate.Truncate(4000);

        if (string.IsNullOrWhiteSpace(draft.Instructions))
        {
            draft.Instructions = "Summarize the document and extract key facts relevant to this source type.";
            warnings.Add("AI response did not include instructions; fallback instructions applied.");
        }

        if (string.IsNullOrWhiteSpace(draft.OutputTemplate))
        {
            draft.OutputTemplate = "{{summary}}";
            warnings.Add("AI response did not include an output template; default template applied.");
        }

        draft.Tags = NormalizeList(draft.Tags, warnings, "tags");
        draft.Descriptors = NormalizeList(draft.Descriptors, warnings, "descriptors");
        draft.FilenamePatterns = ValidatePatterns(draft.FilenamePatterns, warnings);
        draft.Keywords = NormalizeList(draft.Keywords, warnings, "keywords");
        draft.MimeTypes = NormalizeList(draft.MimeTypes, warnings, "mime types");

        var normalizedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in draft.FieldQueries)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
            {
                continue;
            }

            var key = kvp.Key.Trim();
            var value = kvp.Value.Trim();
            if (value.Length > MaxFieldQueryLength)
            {
                warnings.Add($"Field query for '{key}' was truncated to {MaxFieldQueryLength} characters.");
                value = value[..MaxFieldQueryLength];
            }

            normalizedQueries[key] = value;
        }

        draft.FieldQueries = normalizedQueries;

        if (draft.ExpectedPageCountMin.HasValue && draft.ExpectedPageCountMin < 0)
        {
            warnings.Add("Minimum page count was below zero and has been cleared.");
            draft.ExpectedPageCountMin = null;
        }

        if (draft.ExpectedPageCountMax.HasValue && draft.ExpectedPageCountMax < 0)
        {
            warnings.Add("Maximum page count was below zero and has been cleared.");
            draft.ExpectedPageCountMax = null;
        }

        if (draft.ExpectedPageCountMin.HasValue && draft.ExpectedPageCountMax.HasValue &&
            draft.ExpectedPageCountMin > draft.ExpectedPageCountMax)
        {
            warnings.Add("Minimum page count exceeded maximum; values have been swapped.");
            (draft.ExpectedPageCountMin, draft.ExpectedPageCountMax) =
                (draft.ExpectedPageCountMax, draft.ExpectedPageCountMin);
        }
    }

    private static List<string> NormalizeList(List<string> values, List<string> warnings, string label)
    {
        var normalized = values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();

        if (normalized.Count == 0 && values.Count > 0)
        {
            warnings.Add($"All suggested {label} were empty after sanitization.");
        }

        return normalized;
    }

    private static List<string> ValidatePatterns(List<string> patterns, List<string> warnings)
    {
        var valid = new List<string>();
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                valid.Add(pattern);
            }
            catch (Exception ex)
            {
                warnings.Add($"Filename pattern '{pattern}' was removed: {ex.Message}");
            }
        }

        return valid.Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();
    }

    private static string BuildRequestSummary(SourceTypeAiSuggestRequest request)
    {
        var name = string.IsNullOrWhiteSpace(request.DocumentName) ? "unknown" : request.DocumentName.Trim();
        return $"doc={name};targetFields={request.TargetFields.Count};tags={request.DesiredTags.Count}";
    }

    private static string BuildResponseSummary(SourceTypeDraft draft)
    {
        return $"name={draft.Name};tags={draft.Tags.Count};keywords={draft.Keywords.Count};queries={draft.FieldQueries.Count}";
    }
}

internal static class SourceTypeAuthoringExtensions
{
    public static string Truncate(this string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength];
    }
}
