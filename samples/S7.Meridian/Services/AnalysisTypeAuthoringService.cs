using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Samples.Meridian.Contracts;
using Koan.Samples.Meridian.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

public interface IAnalysisTypeAuthoringService
{
    Task<AnalysisTypeAiSuggestResponse> SuggestAsync(AnalysisTypeAiSuggestRequest request, CancellationToken ct);
}

public sealed class AnalysisTypeAuthoringService : IAnalysisTypeAuthoringService
{
    private const int MaxListItems = 16;

    private readonly MeridianOptions _options;
    private readonly IAiAssistAuditor _auditor;
    private readonly ILogger<AnalysisTypeAuthoringService> _logger;

    public AnalysisTypeAuthoringService(
        IOptions<MeridianOptions> options,
        IAiAssistAuditor auditor,
        ILogger<AnalysisTypeAuthoringService> logger)
    {
        _options = options.Value;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task<AnalysisTypeAiSuggestResponse> SuggestAsync(AnalysisTypeAiSuggestRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            throw new ArgumentException("Goal is required.", nameof(request));
        }

        var prompt = BuildPrompt(request);
        var model = request.Model ?? _options.Extraction.Model ?? "granite3.3:8b";
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = model,
            Temperature = 0.1,
            MaxTokens = 700
        };

        string rawResponse;
        try
        {
            rawResponse = await Ai.Chat(chatOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI assist failed for analysis type draft generation.");
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
            ["audience"] = request.Audience ?? string.Empty
        };

        await _auditor.RecordAsync(
            "AnalysisType",
            draft.Name,
            requestSummary,
            responseSummary,
            chatOptions.Model,
            metadata,
            ct).ConfigureAwait(false);

        return new AnalysisTypeAiSuggestResponse
        {
            Draft = draft,
            Warnings = warnings
        };
    }

    private static string BuildPrompt(AnalysisTypeAiSuggestRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are generating an analysis type specification for a narrative AI system.");
        builder.AppendLine("Output ONLY JSON matching this schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"name\": \"string\",");
        builder.AppendLine("  \"description\": \"string\",");
        builder.AppendLine("  \"tags\": [\"string\"],");
        builder.AppendLine("  \"descriptors\": [\"string\"],");
        builder.AppendLine("  \"instructions\": \"string\",");
        builder.AppendLine("  \"outputFields\": [\"fieldName1\", \"fieldName2\", \"fieldName3\"],");
        builder.AppendLine("  \"outputTemplate\": \"string\",");
        builder.AppendLine("  \"requiredSourceTypes\": [\"string\"]");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("CRITICAL: Define 3-7 output field names in outputFields that capture the analysis goal.");
    builder.AppendLine("Use Mustache syntax {{field_name}} in outputTemplate matching the field names exactly.");
    builder.AppendLine("Field names must be snake_case (lowercase letters and underscores only).");
        builder.AppendLine();
        builder.Append("Analysis goal: ").AppendLine(request.Goal.Trim());

        if (!string.IsNullOrWhiteSpace(request.Audience))
        {
            builder.Append("Intended audience: ").AppendLine(request.Audience.Trim());
        }

        if (request.IncludedSourceTypes.Count > 0)
        {
            builder.AppendLine("Available source types:");
            foreach (var sourceType in request.IncludedSourceTypes)
            {
                builder.Append("- ").AppendLine(sourceType.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(request.AdditionalContext))
        {
            builder.Append("Additional context: ").AppendLine(request.AdditionalContext.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("Provide concise instructions suitable for prompt injection into an LLM.");
        return builder.ToString();
    }

    private static AnalysisTypeDraft ParseDraft(string rawResponse, List<string> warnings)
    {
        try
        {
            var json = JObject.Parse(rawResponse);

            // Parse output fields and build simple schema
            var outputFields = ExtractArray(json["outputFields"]);
            var canonicalFields = outputFields
                .Select(FieldPathCanonicalizer.ToTemplateKey)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var schemaJson = BuildSchemaFromFields(canonicalFields);
            
            var draft = new AnalysisTypeDraft
            {
                Name = json["name"]?.Value<string>()?.Trim() ?? string.Empty,
                Description = json["description"]?.Value<string>()?.Trim() ?? string.Empty,
                Instructions = json["instructions"]?.Value<string>()?.Trim() ?? string.Empty,
                OutputTemplate = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(json["outputTemplate"]?.Value<string>()?.Trim() ?? string.Empty),
                OutputSchemaJson = schemaJson,
                Tags = ExtractArray(json["tags"]),
                Descriptors = ExtractArray(json["descriptors"]),
                RequiredSourceTypes = ExtractArray(json["requiredSourceTypes"])
            };

            return draft;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse AI response: {ex.Message}");
            return new AnalysisTypeDraft();
        }
    }

    private static List<string> ExtractArray(JToken? token)
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

    private static string BuildSchemaFromFields(List<string> fields)
    {
        if (fields.Count == 0)
        {
            return "{\"type\":\"object\",\"properties\":{}}";
        }

        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject()
        };

        var properties = (JObject)schema["properties"]!;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var canonical = FieldPathCanonicalizer.ToTemplateKey(field);
            if (string.IsNullOrWhiteSpace(canonical) || !visited.Add(canonical))
            {
                continue;
            }

            properties[canonical] = new JObject { ["type"] = "string" };
        }

        return schema.ToString(Formatting.None);
    }

    private static void SanitizeDraft(AnalysisTypeDraft draft, List<string> warnings)
    {
        draft.Name = draft.Name.Truncate(128);
        draft.Description = draft.Description.Truncate(512);
        draft.Instructions = draft.Instructions.Truncate(2000);
        draft.OutputTemplate = draft.OutputTemplate.Truncate(4000);
        draft.OutputTemplate = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(draft.OutputTemplate);
    draft.OutputSchemaJson = draft.OutputSchemaJson?.Truncate(8000) ?? string.Empty;
    draft.OutputSchemaJson = FieldPathCanonicalizer.CanonicalizeJsonSchema(draft.OutputSchemaJson);

        draft.Tags = draft.Tags
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();

        draft.Descriptors = draft.Descriptors
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();

        draft.RequiredSourceTypes = draft.RequiredSourceTypes
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();

        if (string.IsNullOrWhiteSpace(draft.Instructions))
        {
            warnings.Add("AI response did not include instructions; fallback instructions applied.");
            draft.Instructions = "Synthesize the attached documents into a cohesive narrative.";
        }

        if (string.IsNullOrWhiteSpace(draft.OutputTemplate))
        {
            warnings.Add("AI response did not include an output template; default template applied.");
            draft.OutputTemplate = "# Executive Summary\n\n## Findings\n- {{finding}}\n";
            draft.OutputTemplate = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(draft.OutputTemplate);
        }
        
        if (string.IsNullOrWhiteSpace(draft.OutputSchemaJson))
        {
            warnings.Add("AI response did not include output fields; default schema applied.");
            draft.OutputSchemaJson = "{\"type\":\"object\",\"properties\":{\"summary\":{\"type\":\"string\"}}}";
        }
    }

    private static string BuildRequestSummary(AnalysisTypeAiSuggestRequest request)
    {
        var audience = string.IsNullOrWhiteSpace(request.Audience) ? "n/a" : request.Audience.Trim();
        var goalSnippet = request.Goal.Truncate(40).ReplaceLineEndings(" ").Trim();
        return $"goal=\"{goalSnippet}\";audience={audience};sources={request.IncludedSourceTypes.Count}";
    }

    private static string BuildResponseSummary(AnalysisTypeDraft draft)
    {
        return $"name={draft.Name};tags={draft.Tags.Count};requiredSources={draft.RequiredSourceTypes.Count}";
    }
}
