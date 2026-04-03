using System.Text.Json;
using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Graph;

/// <summary>
/// Extracts entities, facts, and optionally relationships from text chunks
/// using the LLM. Domain-agnostic: the same prompts work for Pokemon cards,
/// healthcare policies, and mathematical proofs.
/// <para>
/// The directive (if present) is injected as domain guidance to shape extraction
/// without changing the prompt structure.
/// </para>
/// </summary>
internal sealed class EntityExtractor
{
    private readonly ILogger<EntityExtractor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EntityExtractor(ILogger<EntityExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract entities and facts from a text chunk (Lightweight + Full strategies).
    /// </summary>
    public async Task<ExtractionResult> ExtractEntities(
        string chunkText,
        string? documentTitle,
        string? sectionTitle,
        string? directive,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chunkText))
            return new ExtractionResult();

        var prompt = BuildEntityExtractionPrompt(chunkText, documentTitle, sectionTitle, directive);

        try
        {
            var response = await Koan.AI.Client.Chat(prompt, ct);
            return ParseExtractionResult(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Entity extraction failed for chunk from '{Document}/{Section}'",
                documentTitle, sectionTitle);
            return new ExtractionResult();
        }
    }

    /// <summary>
    /// Extract explicit relationships between entities (Full strategy only).
    /// </summary>
    public async Task<RelationshipExtractionResult> ExtractRelationships(
        string chunkText,
        IReadOnlyList<string> knownEntityNames,
        string? directive,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chunkText) || knownEntityNames.Count == 0)
            return new RelationshipExtractionResult();

        var prompt = BuildRelationshipExtractionPrompt(chunkText, knownEntityNames, directive);

        try
        {
            var response = await Koan.AI.Client.Chat(prompt, ct);
            return ParseRelationshipResult(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Relationship extraction failed");
            return new RelationshipExtractionResult();
        }
    }

    // ── Prompt Construction ─────────────────────────────────────────────

    private static string BuildEntityExtractionPrompt(
        string chunkText,
        string? documentTitle,
        string? sectionTitle,
        string? directive)
    {
        var contextLine = documentTitle is not null
            ? $"This chunk is from a document titled \"{documentTitle}\""
              + (sectionTitle is not null ? $", section \"{sectionTitle}\"" : "")
              + ".\n\n"
            : "";

        var directiveLine = directive is not null
            ? $"DOMAIN GUIDANCE: {directive}\n\n"
            : "";

        return $$"""
            {{directiveLine}}You are an entity extraction agent. Given the text chunk below, extract structured data.

            {{contextLine}}---
            {{chunkText}}
            ---

            Extract and return a JSON object with exactly these fields:

            {
              "entities": [
                {
                  "name": "Canonical name for the concept, object, standard, rule, person, or category",
                  "description": "One-line description of this entity in the context of this corpus"
                }
              ],
              "facts": [
                {
                  "statement": "A specific assertion, rule, or claim. Must be self-contained (understandable without the source document)",
                  "relatedEntities": ["entity name 1", "entity name 2"]
                }
              ]
            }

            Rules:
            - Extract named concepts, objects, standards, rules, categories, or any noun another document might reference.
            - Each fact should be independently meaningful without the source document.
            - Use canonical names (e.g., "HIPAA" not "the HIPAA regulation").
            - Return only valid JSON. No markdown, no commentary.
            """;
    }

    private static string BuildRelationshipExtractionPrompt(
        string chunkText,
        IReadOnlyList<string> knownEntityNames,
        string? directive)
    {
        var entityList = string.Join(", ", knownEntityNames.Select(n => $"\"{n}\""));

        var directiveLine = directive is not null
            ? $"DOMAIN GUIDANCE: {directive}\n\n"
            : "";

        return $$"""
            {{directiveLine}}You are a relationship extraction agent. Given the text and the known entities, identify how they relate.

            Known entities: [{{entityList}}]

            ---
            {{chunkText}}
            ---

            Return a JSON object:

            {
              "relationships": [
                {
                  "from": "source entity name (must be from the known entities list)",
                  "to": "target entity name (must be from the known entities list)",
                  "label": "natural-language relationship (e.g., requires, is-a, governed-by, contains, prohibits)"
                }
              ]
            }

            Rules:
            - Only use entity names from the known entities list.
            - Use natural-language relationship labels.
            - Only extract relationships explicitly stated or strongly implied in the text.
            - Return only valid JSON.
            """;
    }

    // ── Response Parsing ────────────────────────────────────────────────

    private ExtractionResult ParseExtractionResult(string response)
    {
        try
        {
            var cleaned = CleanJsonResponse(response);
            return JsonSerializer.Deserialize<ExtractionResult>(cleaned, JsonOptions)
                ?? new ExtractionResult();
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse entity extraction JSON, attempting recovery");
            return new ExtractionResult();
        }
    }

    private RelationshipExtractionResult ParseRelationshipResult(string response)
    {
        try
        {
            var cleaned = CleanJsonResponse(response);
            return JsonSerializer.Deserialize<RelationshipExtractionResult>(cleaned, JsonOptions)
                ?? new RelationshipExtractionResult();
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse relationship extraction JSON");
            return new RelationshipExtractionResult();
        }
    }

    /// <summary>
    /// Strip markdown code fences and surrounding whitespace that LLMs often emit.
    /// </summary>
    private static string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();

        // Strip ```json ... ``` wrapper
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];

            trimmed = trimmed.Trim();
        }

        return trimmed;
    }
}
