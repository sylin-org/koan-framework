using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Services;

public interface INotesExtractionService
{
    /// <summary>
    /// Extract field values from free-text Authoritative Notes using AI interpretation
    /// with fuzzy field name matching.
    /// </summary>
    Task<List<ExtractedField>> ExtractFromNotesAsync(
        DocumentPipeline pipeline,
        string virtualDocumentId,
        CancellationToken ct);
}

/// <summary>
/// Extracts structured field values from free-text Authoritative Notes using AI.
///
/// Key Features:
/// - Free-text interpretation (no regex/procedural parsing)
/// - Fuzzy field matching ("Request #" → "Request Item Number")
/// - Schema-aware extraction
/// - 100% confidence (user-provided)
/// - Precedence=1 (highest priority)
/// </summary>
public sealed class NotesExtractionService : INotesExtractionService
{
    private readonly ILogger<NotesExtractionService> _logger;
    private readonly IRunLogWriter _runLog;
    private readonly MeridianOptions _options;

    public NotesExtractionService(
        ILogger<NotesExtractionService> logger,
        IRunLogWriter runLog,
        MeridianOptions options)
    {
        _logger = logger;
        _runLog = runLog;
        _options = options;
    }

    public async Task<List<ExtractedField>> ExtractFromNotesAsync(
        DocumentPipeline pipeline,
        string virtualDocumentId,
        CancellationToken ct)
    {
        var results = new List<ExtractedField>();

        if (string.IsNullOrWhiteSpace(pipeline.AuthoritativeNotes))
        {
            return results;
        }

        var schema = pipeline.TryParseSchema();
        if (schema == null)
        {
            _logger.LogWarning(
                "Pipeline {PipelineId} schema invalid; skipping Notes extraction.",
                pipeline.Id);
            return results;
        }

    var fieldPaths = EnumerateLeafSchemas(schema).ToList();

        _logger.LogInformation(
            "Extracting from Authoritative Notes for pipeline {PipelineId}, {FieldCount} fields in schema",
            pipeline.Id,
            fieldPaths.Count);

        var extractionStarted = DateTime.UtcNow;

        // Build prompt for AI to extract all fields from Notes in one call
        var prompt = BuildNotesExtractionPrompt(pipeline, fieldPaths);

        try
        {
            var chatOptions = new AiChatOptions
            {
                Message = prompt,
                Model = _options.Extraction.Model ?? "granite3.3:8b",
                Temperature = 0.1, // Low temperature for deterministic extraction
                MaxTokens = 2000
            };

            var response = await Ai.Chat(chatOptions, ct);

            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning(
                    "Empty AI response for Notes extraction in pipeline {PipelineId}",
                    pipeline.Id);
                return results;
            }

            // Parse AI response - expecting JSON with field extractions
            var extractedData = ParseNotesResponse(response, pipeline.Id, virtualDocumentId, fieldPaths);

            results.AddRange(extractedData);

            var extractionTime = DateTime.UtcNow - extractionStarted;

            await _runLog.AppendAsync(new RunLog
            {
                PipelineId = pipeline.Id,
                Stage = "notes-extraction",
                FieldPath = null,
                StartedAt = extractionStarted,
                FinishedAt = DateTime.UtcNow,
                Status = "success",
                Metadata = new Dictionary<string, string>
                {
                    ["fieldCount"] = results.Count.ToString(),
                    ["extractionTimeSeconds"] = extractionTime.TotalSeconds.ToString("F2")
                }
            }, ct);

            _logger.LogInformation(
                "Extracted {Count} fields from Authoritative Notes for pipeline {PipelineId} in {ElapsedMs}ms",
                results.Count,
                pipeline.Id,
                extractionTime.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to extract from Authoritative Notes for pipeline {PipelineId}",
                pipeline.Id);

            await _runLog.AppendAsync(new RunLog
            {
                PipelineId = pipeline.Id,
                Stage = "notes-extraction",
                FieldPath = null,
                StartedAt = extractionStarted,
                FinishedAt = DateTime.UtcNow,
                Status = "failed",
                ErrorMessage = ex.Message
            }, ct);
        }

        return results;
    }

    private string BuildNotesExtractionPrompt(
        DocumentPipeline pipeline,
        List<(string path, JSchema schema)> fieldPaths)
    {
        var schemaDescription = BuildSchemaDescription(fieldPaths);

        return $$$"""
# Task: Extract Structured Data from Authoritative Notes

You are analyzing free-text notes provided by a user that contain authoritative data values
for specific fields. Your task is to identify and extract values for the fields listed below.

## Authoritative Notes (User-Provided):
```
{{{pipeline.AuthoritativeNotes}}}
```

## Target Fields (Schema):
{{{schemaDescription}}}

## Instructions:
1. **Fuzzy Field Matching**: The user may use different names for fields
   - Example: "Request #" should match "Request Item Number"
   - Example: "CEO" should match "Chief Executive Officer Name"
   - Example: "Employee Count" matches "Number of Employees"

2. **Free-Text Interpretation**: Extract values from natural language
   - Example: "CEO Name: Jane Smith" → extract "Jane Smith"
   - Example: "475 employees as of Sept 2024" → extract "475"

3. **Only Extract What's Present**: If a field is not mentioned, omit it from output

4. **Preserve Original Values**: Don't normalize or transform values
 
5. **Use Canonical Field Paths**: Return the canonical snake_case path exactly as listed (prefixed with $.).

## Output Format (JSON):
{
  "extractions": [
    {
        "fieldPath": "$.canonical_field_path",
      "value": <extracted_value_matching_field_type>,
      "matchedText": "original text snippet from notes",
      "confidence": 1.0
    }
  ]
}

## Example:
If Notes = "CEO Name: Dana Martinez, Employee Count: 475"
And Schema = ["$.company_info.ceo", "$.company_info.employee_count"]
Then Output:
{
  "extractions": [
    {
            "fieldPath": "$.company_info.ceo",
      "value": "Dana Martinez",
      "matchedText": "CEO Name: Dana Martinez",
      "confidence": 1.0
    },
    {
            "fieldPath": "$.company_info.employee_count",
      "value": 475,
      "matchedText": "Employee Count: 475",
      "confidence": 1.0
    }
  ]
}

Extract all matching fields now:
""";
    }

    private string BuildSchemaDescription(List<(string path, JSchema schema)> fieldPaths)
    {
        var lines = new List<string>();

        foreach (var (path, schema) in fieldPaths)
        {
            var displayName = FieldPathCanonicalizer.ToDisplayName(path);
            var typeStr = schema.Type?.ToString() ?? "any";
            var desc = schema.Description ?? "(no description)";
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                lines.Add($"- `{path}` ({displayName}, {typeStr}): {desc}");
            }
            else
            {
                lines.Add($"- `{path}` ({typeStr}): {desc}");
            }
        }

        return string.Join("\n", lines);
    }

    private List<ExtractedField> ParseNotesResponse(
        string response,
        string pipelineId,
        string virtualDocumentId,
        List<(string path, JSchema schema)> schemaFieldPaths)
    {
        var results = new List<ExtractedField>();
        var schemaLookup = schemaFieldPaths.ToDictionary(fp => fp.path, fp => fp.schema, StringComparer.Ordinal);

        try
        {
            // Strip markdown code fences if present (LLMs often wrap JSON in ```json...```)
            var cleanResponse = response.Trim();
            if (cleanResponse.StartsWith("```"))
            {
                var lines = cleanResponse.Split('\n');
                cleanResponse = string.Join("\n", lines.Skip(1).Take(lines.Length - 2));
            }

            var json = JObject.Parse(cleanResponse);
            var extractions = json["extractions"] as JArray;

            if (extractions == null)
            {
                _logger.LogWarning("AI response missing 'extractions' array");
                return results;
            }

            foreach (var extraction in extractions)
            {
                var fieldPath = extraction["fieldPath"]?.ToString();
                var value = extraction["value"];
                var matchedText = extraction["matchedText"]?.ToString() ?? "";
                var confidence = extraction["confidence"]?.Value<double>() ?? 1.0;

                if (string.IsNullOrWhiteSpace(fieldPath) || value == null)
                {
                    continue;
                }

                // Normalize field path to match schema (case-insensitive)
                var canonicalPath = FieldPathCanonicalizer.Canonicalize(fieldPath);

                if (!schemaLookup.TryGetValue(canonicalPath, out var fieldSchema))
                {
                    _logger.LogWarning(
                        "Field path {FieldPath} from Notes not found in schema; skipping",
                        fieldPath);
                    continue;
                }

                var field = new ExtractedField
                {
                    PipelineId = pipelineId,
                    FieldPath = canonicalPath,
                    ValueJson = value.ToString(Formatting.None),
                    Confidence = confidence,
                    SourceDocumentId = virtualDocumentId,
                    PassageId = null, // Notes don't have passages
                    Source = FieldSource.AuthoritativeNotes,
                    Precedence = 1, // Highest priority
                    Evidence = new TextSpanEvidence
                    {
                        SourceDocumentId = virtualDocumentId,
                        OriginalText = matchedText,
                        Metadata = new Dictionary<string, string>
                        {
                            ["Source"] = "Authoritative Notes (User Override)",
                            ["ExtractedAt"] = DateTime.UtcNow.ToString("O")
                        }
                    },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                results.Add(field);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI response as JSON: {Response}", response);
        }

        return results;
    }

    private IEnumerable<(string path, JSchema schema)> EnumerateLeafSchemas(JSchema root, string prefix = "$")
    {
        if (root.Type == JSchemaType.Array && root.Items.Count > 0)
        {
            var nextPrefix = prefix.EndsWith("[]", StringComparison.Ordinal)
                ? prefix
                : $"{prefix}[]";

            foreach (var nested in EnumerateLeafSchemas(root.Items[0], nextPrefix))
            {
                yield return nested;
            }

            yield break;
        }

        if (root.Type == JSchemaType.Object && root.Properties.Count > 0)
        {
            foreach (var property in root.Properties)
            {
                var nextPrefix = prefix == "$"
                    ? $"$.{property.Key}"
                    : $"{prefix}.{property.Key}";

                foreach (var nested in EnumerateLeafSchemas(property.Value, nextPrefix))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        yield return (FieldPathCanonicalizer.Canonicalize(prefix), root);
    }
}
