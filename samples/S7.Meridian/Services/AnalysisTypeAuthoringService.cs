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

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        var prompt = BuildPrompt(request);
        // Model selection removed from request; use options default or fallback
        var model = _options.Extraction.Model ?? "granite3.3:8b";
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = model,
            Temperature = 0.1,
            MaxTokens = 4000
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
        var draft = ParseDraft(rawResponse, request.Prompt, warnings);
        SanitizeDraft(draft, warnings);

        var requestSummary = BuildRequestSummary(request);
        var responseSummary = BuildResponseSummary(draft);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["warnings"] = warnings.Count.ToString(),
            ["prompt"] = request.Prompt.Truncate(120)
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
        builder.AppendLine("  \"outputTemplate\": \"string\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("FIELD REQUIREMENTS:");
        builder.AppendLine("- Define 5-20 output field names based on analysis complexity");
        builder.AppendLine("- Use snake_case (lowercase letters and underscores only)");
        builder.AppendLine("- Choose field names that comprehensively capture the analysis goal");
        builder.AppendLine("- Include fields for context, findings, recommendations, and metadata");
        builder.AppendLine();
        builder.AppendLine("TEMPLATE REQUIREMENTS:");
        builder.AppendLine("- Use markdown format with semantic sections (## Section Name)");
        builder.AppendLine("- Use Mustache syntax {{field_name}} matching field names exactly");
        builder.AppendLine("- Group related fields into logical sections");
        builder.AppendLine("- Use tables, lists, and formatting for readability");
        builder.AppendLine("- Include descriptive headers that explain each section's purpose");
        builder.AppendLine();
        builder.AppendLine("INSTRUCTION REQUIREMENTS:");
        builder.AppendLine("- Begin with role context (e.g., 'As a [role] at [organization]...')");
        builder.AppendLine("- Explain the analysis purpose and intended audience");
        builder.AppendLine("- Provide specific extraction criteria for each major field");
        builder.AppendLine("- Include quality expectations and formatting guidance");
        builder.AppendLine("- Write 200-500 words of detailed, actionable instructions");
        builder.AppendLine();
        builder.Append("Analysis type prompt: ").AppendLine(request.Prompt.Trim());
        builder.AppendLine();
        builder.AppendLine("Derive ALL output field names, instructions and template content solely from the provided prompt.");
        builder.AppendLine("If the prompt implies domain-specific metadata fields, include them.");
        builder.AppendLine("Do NOT invent unrelated fields; stay tightly aligned to the prompt context.");

        builder.AppendLine();
        builder.AppendLine("EXAMPLE OF RICH TEMPLATE:");
        builder.AppendLine("## Review Details");
        builder.AppendLine("- **Document Title**: {{document_title}}");
        builder.AppendLine("- **Review Date**: {{review_date}}");
        builder.AppendLine();
        builder.AppendLine("## Executive Summary");
        builder.AppendLine("{{executive_summary}}");
        builder.AppendLine();
        builder.AppendLine("## Key Findings");
        builder.AppendLine("{{key_findings}}");
        builder.AppendLine();
        builder.AppendLine("Generate comprehensive instructions suitable for prompt injection into an LLM.");
        builder.AppendLine("Ensure instructions provide clear role context and specific extraction criteria.");
        return builder.ToString();
    }

    private static AnalysisTypeDraft ParseDraft(string rawResponse, string originalPrompt, List<string> warnings)
    {
        try
        {
            var cleaned = rawResponse?.Trim() ?? string.Empty;

            // Strip markdown code fences if present
            if (cleaned.StartsWith("```"))
            {
                // Remove initial fence line
                var newlineIndex = cleaned.IndexOf('\n');
                if (newlineIndex > -1)
                {
                    cleaned = cleaned[(newlineIndex + 1)..];
                }
                // Remove trailing fence
                var fenceLast = cleaned.LastIndexOf("```");
                if (fenceLast > -1)
                {
                    cleaned = cleaned[..fenceLast].Trim();
                }
            }

            // Attempt direct parse first
            JObject json;
            try
            {
                json = JObject.Parse(cleaned);
            }
            catch
            {
                // Fallback: extract first JSON object substring
                var firstBrace = cleaned.IndexOf('{');
                var lastBrace = cleaned.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    var candidate = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
                    try
                    {
                        json = JObject.Parse(candidate);
                        warnings.Add("AI response contained extraneous text; JSON extracted via substring.");
                    }
                    catch (Exception ex2)
                    {
                        warnings.Add($"Failed to parse AI response after substring attempt: {ex2.Message}");
                        return BuildEmptyDraftWithFallback(originalPrompt, warnings);
                    }
                }
                else
                {
                    warnings.Add("AI response lacked recognizable JSON braces.");
                    return BuildEmptyDraftWithFallback(originalPrompt, warnings);
                }
            }

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
                JsonSchema = schemaJson,
                Tags = ExtractArray(json["tags"]),
                Descriptors = ExtractArray(json["descriptors"])
            };

            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                draft.Name = DeriveNameFromPrompt(originalPrompt);
                warnings.Add("Name missing in AI response; derived from prompt.");
            }
            if (string.IsNullOrWhiteSpace(draft.Description))
            {
                draft.Description = ($"Analysis derived from prompt: {originalPrompt}").Truncate(512);
                warnings.Add("Description missing in AI response; fallback applied.");
            }

            return draft;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse AI response: {ex.Message}");
            return BuildEmptyDraftWithFallback(originalPrompt, warnings);
        }
    }

    private static AnalysisTypeDraft BuildEmptyDraftWithFallback(string originalPrompt, List<string> warnings)
    {
        var name = DeriveNameFromPrompt(originalPrompt);
        var description = ($"Analysis derived from prompt: {originalPrompt}").Truncate(512);
        return new AnalysisTypeDraft
        {
            Name = name,
            Description = description,
            Instructions = string.Empty,
            OutputTemplate = string.Empty,
            JsonSchema = string.Empty,
            Tags = new List<string>(),
            Descriptors = new List<string>()
        };
    }

    private static string DeriveNameFromPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "UntitledAnalysis";
        }
        var cleaned = new string(prompt.Take(160).ToArray());
        // Replace non-letter/digit with space then pick first 5 words
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "[^A-Za-z0-9]+", " ").Trim();
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6).ToList();
        if (words.Count == 0) return "UntitledAnalysis";
        var title = string.Join(" ", words).Trim();
        // TitleCase
        title = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title.ToLowerInvariant());
        return title.Length > 48 ? title[..48] : title;
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
        draft.Instructions = draft.Instructions.Truncate(5000);
        draft.OutputTemplate = draft.OutputTemplate.Truncate(10000);
        draft.OutputTemplate = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(draft.OutputTemplate);
        draft.JsonSchema = draft.JsonSchema?.Truncate(16000) ?? string.Empty;
        draft.JsonSchema = FieldPathCanonicalizer.CanonicalizeJsonSchema(draft.JsonSchema); draft.Tags = draft.Tags
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

        if (string.IsNullOrWhiteSpace(draft.JsonSchema))
        {
            warnings.Add("AI response did not include output fields; default schema applied.");
            draft.JsonSchema = "{\"type\":\"object\",\"properties\":{\"summary\":{\"type\":\"string\"}}}";
        }
    }

    private static string BuildRequestSummary(AnalysisTypeAiSuggestRequest request)
    {
        var promptSnippet = request.Prompt.Truncate(60).ReplaceLineEndings(" ").Trim();
        return $"prompt=\"{promptSnippet}\"";
    }

    private static string BuildResponseSummary(AnalysisTypeDraft draft)
    {
        return $"name={draft.Name};tags={draft.Tags.Count};descriptors={draft.Descriptors.Count}";
    }
}
