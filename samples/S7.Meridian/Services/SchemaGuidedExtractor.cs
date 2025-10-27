using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

public interface ISchemaGuidedExtractor
{
    Task<List<ExtractedField>> ExtractBatchAsync(
        SourceDocument document,
        SemanticBatch batch,
        FactCatalog catalog,
        string pipelineId,
        CancellationToken ct);

    Task<List<ExtractedField>> ExtractBatchFromNotesAsync(
        string notes,
        SemanticBatch batch,
        FactCatalog catalog,
        string pipelineId,
        CancellationToken ct);
}

/// <summary>
/// Stage 3: Targeted extraction of facts from documents using schema-guided prompts.
/// Extracts facts in semantic batches where related fields benefit from shared context.
/// </summary>
public sealed class SchemaGuidedExtractor : ISchemaGuidedExtractor
{
    private static readonly Regex JsonFence = new("```json|```", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly MeridianOptions _options;
    private readonly ILogger<SchemaGuidedExtractor> _logger;

    public SchemaGuidedExtractor(IOptions<MeridianOptions> options, ILogger<SchemaGuidedExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<ExtractedField>> ExtractBatchAsync(
        SourceDocument document,
        SemanticBatch batch,
        FactCatalog catalog,
        string pipelineId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            _logger.LogWarning("Document {DocumentId} has no extracted text; skipping batch '{Batch}'",
                document.Id, batch.CategoryName);
            return new List<ExtractedField>();
        }

        // Load document style for extraction strategy
        DocumentStyle? documentStyle = null;
        if (!string.IsNullOrWhiteSpace(document.DocumentStyleId))
        {
            documentStyle = await DocumentStyle.Get(document.DocumentStyleId, ct).ConfigureAwait(false);
        }

        // Build extraction context based on document style
        string extractionContext;
        bool usedPassageRetrieval = false;

        if (documentStyle?.UsePassageRetrieval == true)
        {
            // Use RAG to get focused context
            var passages = await RetrieveRelevantPassagesAsync(
                document, batch, catalog, documentStyle.PassageRetrievalTopK, ct).ConfigureAwait(false);

            if (passages.Count > 0)
            {
                if (documentStyle.ExpandPassageContext)
                {
                    // Expand passages with surrounding context for dialogues
                    passages = await ExpandPassageContextAsync(
                        passages, documentStyle.ContextWindowSize, ct).ConfigureAwait(false);
                }

                extractionContext = BuildContextFromPassages(passages);
                usedPassageRetrieval = true;

                _logger.LogDebug("Using RAG context: {PassageCount} passages ({Expanded}) for batch '{Batch}' in {DocumentStyle} document",
                    passages.Count, documentStyle.ExpandPassageContext ? "expanded" : "focused",
                    batch.CategoryName, documentStyle.Code);
            }
            else
            {
                // Fallback to full document if no passages found
                extractionContext = document.ExtractedText;
                _logger.LogDebug("No relevant passages found; falling back to full document for batch '{Batch}'",
                    batch.CategoryName);
            }
        }
        else
        {
            // Use full document context
            extractionContext = document.ExtractedText;
        }

        var prompt = BuildExtractionPrompt(
            document.OriginalFileName,
            extractionContext,
            batch,
            catalog,
            documentStyle,
            usedPassageRetrieval);

        var fields = await ExtractFieldsAsync(prompt, batch, catalog, pipelineId, document.Id, FieldSource.DocumentExtraction, ct).ConfigureAwait(false);

        _logger.LogInformation("Extracted {FieldCount}/{TotalFields} fields from document {DocumentId} for batch '{Batch}' (style: {Style}, RAG: {UsedRAG})",
            fields.Count, batch.FieldPaths.Count, document.Id, batch.CategoryName,
            documentStyle?.Code ?? "unknown", usedPassageRetrieval);

        return fields;
    }

    public async Task<List<ExtractedField>> ExtractBatchFromNotesAsync(
        string notes,
        SemanticBatch batch,
        FactCatalog catalog,
        string pipelineId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return new List<ExtractedField>();
        }

        var notesDocId = $"notes:{pipelineId}";
        var prompt = BuildNotesExtractionPrompt(notes, batch, catalog);
        var fields = await ExtractFieldsAsync(prompt, batch, catalog, pipelineId, notesDocId, FieldSource.AuthoritativeNotes, ct).ConfigureAwait(false);

        _logger.LogInformation("Extracted {FieldCount}/{TotalFields} fields from authoritative notes for batch '{Batch}'",
            fields.Count, batch.FieldPaths.Count, batch.CategoryName);

        return fields;
    }

    private async Task<List<ExtractedField>> ExtractFieldsAsync(
        string prompt,
        SemanticBatch batch,
        FactCatalog catalog,
        string pipelineId,
        string sourceDocumentId,
        FieldSource source,
        CancellationToken ct)
    {
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = _options.Facts.ExtractionModel,
            Temperature = _options.Facts.ExtractionTemperature,
            MaxTokens = 0,
            ResponseFormat = "json"
        };

        string raw;
        try
        {
            _logger.LogDebug("Extracting batch '{Batch}' ({FieldCount} fields) using {Model}",
                batch.CategoryName, batch.FieldPaths.Count, _options.Facts.ExtractionModel);

            raw = await Ai.Chat(chatOptions, ct).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var preview = raw.Length > 1000 ? raw.Substring(0, 1000) + "..." : raw;
                _logger.LogTrace("RAW extraction response for batch '{Batch}': {Response}",
                    batch.CategoryName, preview);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to extract batch '{Batch}' with model {Model}",
                batch.CategoryName, _options.Facts.ExtractionModel);
            throw;
        }

        var fields = ParseExtractedFields(raw, batch, catalog, pipelineId, sourceDocumentId, source);

        _logger.LogDebug("Parsed {ExtractedCount} fields from LLM response for batch '{Batch}'",
            fields.Count, batch.CategoryName);

        return fields;
    }

    private string BuildExtractionPrompt(
        string documentName,
        string documentText,
        SemanticBatch batch,
        FactCatalog catalog,
        DocumentStyle? documentStyle = null,
        bool usedPassageRetrieval = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extract structured data from this document.");
        sb.AppendLine();

        // Add document style-specific instructions
        if (documentStyle != null && !string.IsNullOrWhiteSpace(documentStyle.ExtractionStrategy))
        {
            sb.AppendLine($"DOCUMENT STYLE: {documentStyle.Name} ({documentStyle.Code})");
            sb.AppendLine();
            sb.AppendLine("STYLE-SPECIFIC EXTRACTION STRATEGY:");
            sb.AppendLine(documentStyle.ExtractionStrategy);
            sb.AppendLine();

            if (usedPassageRetrieval)
            {
                sb.AppendLine("NOTE: You are seeing FOCUSED EXCERPTS from the document, not the full text.");
                sb.AppendLine("These passages were selected as most relevant to the fields you need to extract.");
                sb.AppendLine();
            }
        }

        sb.AppendLine($"EXTRACTION BATCH: {batch.CategoryName}");
        sb.AppendLine($"DESCRIPTION: {batch.CategoryDescription}");
        sb.AppendLine();
        sb.AppendLine("FIELDS TO EXTRACT:");

        var factsInBatch = catalog.Facts
            .Where(f => batch.FieldPaths.Contains(f.FieldPath, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var fact in factsInBatch)
        {
            sb.AppendLine();
            sb.Append($"- {fact.FieldPath}");
            if (!string.IsNullOrWhiteSpace(fact.Description))
            {
                sb.AppendLine($"  Description: {fact.Description}");
            }
            if (fact.Examples.Any())
            {
                sb.AppendLine($"  Examples: {string.Join(", ", fact.Examples)}");
            }
            sb.AppendLine($"  Type: {fact.DataType}");
        }

        sb.AppendLine();
        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("1. Extract ONLY fields that are explicitly present in the document");
        sb.AppendLine("2. For each field found, provide:");
        sb.AppendLine("   - value: The extracted value (structured, not a sentence)");
        sb.AppendLine("   - evidence: Verbatim quote from document supporting this extraction");
        sb.AppendLine("   - confidence: \"high\", \"medium\", or \"low\"");
        sb.AppendLine("3. If a field is not found, DO NOT include it in the output");
        sb.AppendLine("4. Extract exact values, not paraphrases");
        sb.AppendLine();
        sb.AppendLine($"DOCUMENT: {documentName}");
        sb.AppendLine("---");
        sb.AppendLine(documentText);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT (strict JSON):");
        sb.AppendLine("{");
        sb.AppendLine("  \"fields\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"fieldPath\": \"$.servicenow_id\",");
        sb.AppendLine("      \"value\": \"RITM 1102144\",");
        sb.AppendLine("      \"evidence\": \"What was the SNOW ticket number requesting the installation? RITM1102144.\",");
        sb.AppendLine("      \"confidence\": \"high\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private string BuildNotesExtractionPrompt(
        string notes,
        SemanticBatch batch,
        FactCatalog catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extract structured data from these AUTHORITATIVE NOTES.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: These are authoritative notes that override document information.");
        sb.AppendLine();
        sb.AppendLine($"EXTRACTION BATCH: {batch.CategoryName}");
        sb.AppendLine($"DESCRIPTION: {batch.CategoryDescription}");
        sb.AppendLine();
        sb.AppendLine("FIELDS TO EXTRACT:");

        var factsInBatch = catalog.Facts
            .Where(f => batch.FieldPaths.Contains(f.FieldPath, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var fact in factsInBatch)
        {
            sb.AppendLine();
            sb.Append($"- {fact.FieldPath}");
            if (!string.IsNullOrWhiteSpace(fact.Description))
            {
                sb.AppendLine($"  Description: {fact.Description}");
            }
            if (fact.Examples.Any())
            {
                sb.AppendLine($"  Examples: {string.Join(", ", fact.Examples)}");
            }
            sb.AppendLine($"  Type: {fact.DataType}");
        }

        sb.AppendLine();
        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("1. Extract ONLY fields that are explicitly present in the notes");
        sb.AppendLine("2. For each field found, provide:");
        sb.AppendLine("   - value: The extracted value (structured, not a sentence)");
        sb.AppendLine("   - evidence: Verbatim quote from notes supporting this extraction");
        sb.AppendLine("   - confidence: \"high\", \"medium\", or \"low\"");
        sb.AppendLine("3. If a field is not found, DO NOT include it in the output");
        sb.AppendLine();
        sb.AppendLine("AUTHORITATIVE NOTES:");
        sb.AppendLine("---");
        sb.AppendLine(notes);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT (strict JSON):");
        sb.AppendLine("{");
        sb.AppendLine("  \"fields\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"fieldPath\": \"$.servicenow_id\",");
        sb.AppendLine("      \"value\": \"RITM 1102144\",");
        sb.AppendLine("      \"evidence\": \"Ticket RITM 1102144 was used for this review.\",");
        sb.AppendLine("      \"confidence\": \"high\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private List<ExtractedField> ParseExtractedFields(
        string rawResponse,
        SemanticBatch batch,
        FactCatalog catalog,
        string pipelineId,
        string sourceDocumentId,
        FieldSource source)
    {
        var results = new List<ExtractedField>();

        try
        {
            var cleaned = JsonFence.Replace(rawResponse, string.Empty).Trim();
            var json = JObject.Parse(cleaned);

            var fieldsToken = json["fields"];
            if (fieldsToken is not JArray array || array.Count == 0)
            {
                _logger.LogDebug("No fields array found in extraction response for batch '{Batch}'",
                    batch.CategoryName);
                return results;
            }

            foreach (var token in array.OfType<JObject>())
            {
                var fieldPath = token.Value<string>("fieldPath")?.Trim();

                // Handle value as either string or array
                var valueToken = token["value"];
                string? value = null;
                if (valueToken != null)
                {
                    if (valueToken.Type == JTokenType.Array)
                    {
                        // Convert array to comma-separated string
                        var items = valueToken.ToObject<string[]>()?.Where(s => !string.IsNullOrWhiteSpace(s));
                        value = items != null ? string.Join(", ", items) : null;
                        _logger.LogDebug("LLM returned array value for '{FieldPath}', converted to: {Value}",
                            fieldPath, value);
                    }
                    else
                    {
                        value = valueToken.Value<string>()?.Trim();
                    }
                }

                // Handle evidence as either string or array
                var evidenceToken = token["evidence"];
                string? evidence = null;
                if (evidenceToken != null)
                {
                    if (evidenceToken.Type == JTokenType.Array)
                    {
                        // Convert array to comma-separated string
                        var items = evidenceToken.ToObject<string[]>()?.Where(s => !string.IsNullOrWhiteSpace(s));
                        evidence = items != null ? string.Join(", ", items) : null;
                    }
                    else
                    {
                        evidence = evidenceToken.Value<string>()?.Trim();
                    }
                }

                var confidenceStr = token.Value<string>("confidence")?.Trim()?.ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(fieldPath) || string.IsNullOrWhiteSpace(value))
                {
                    _logger.LogDebug("Skipping field with missing fieldPath or value");
                    continue;
                }

                // Validate field is in this batch
                if (!batch.FieldPaths.Contains(fieldPath, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("LLM returned field '{FieldPath}' not in batch '{Batch}' - skipping",
                        fieldPath, batch.CategoryName);
                    continue;
                }

                var confidence = ParseConfidence(confidenceStr, source);
                var precedence = source == FieldSource.AuthoritativeNotes ? 1 : 3;

                results.Add(new ExtractedField
                {
                    PipelineId = pipelineId,
                    FieldPath = fieldPath,
                    ValueJson = JsonConvert.SerializeObject(value),
                    Confidence = confidence,
                    SourceDocumentId = sourceDocumentId,
                    Source = source,
                    Precedence = precedence,
                    Evidence = new TextSpanEvidence
                    {
                        SourceDocumentId = sourceDocumentId,
                        OriginalText = evidence ?? value,
                        Metadata = new Dictionary<string, string>
                        {
                            ["batchId"] = batch.BatchId,
                            ["batchCategory"] = batch.CategoryName,
                            ["extractionMethod"] = "schema_guided"
                        }
                    },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse extraction response for batch '{Batch}'",
                batch.CategoryName);
            return results;
        }
    }

    private double ParseConfidence(string? confidenceStr, FieldSource source)
    {
        // Notes always get high confidence
        if (source == FieldSource.AuthoritativeNotes)
        {
            return 1.0;
        }

        return confidenceStr switch
        {
            "high" => 0.9,
            "medium" => 0.7,
            "low" => 0.5,
            _ => 0.8 // Default to high-medium
        };
    }

    /// <summary>
    /// Retrieve relevant passages from the document using vector similarity search.
    /// Formulates a query from the semantic batch and fact catalog.
    /// </summary>
    private async Task<List<Passage>> RetrieveRelevantPassagesAsync(
        SourceDocument document,
        SemanticBatch batch,
        FactCatalog catalog,
        int topK,
        CancellationToken ct)
    {
        // Check if vector workflow is available
        if (!VectorWorkflow<Passage>.IsAvailable(MeridianConstants.VectorProfile))
        {
            _logger.LogDebug("Vector workflow unavailable; cannot use passage retrieval for document {DocumentId}",
                document.Id);
            return new List<Passage>();
        }

        // Build search query from batch and field descriptions
        var queryParts = new List<string>
        {
            batch.CategoryName,
            batch.CategoryDescription
        };

        var factsInBatch = catalog.Facts
            .Where(f => batch.FieldPaths.Contains(f.FieldPath, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var fact in factsInBatch)
        {
            queryParts.Add(fact.FieldName);
            if (!string.IsNullOrWhiteSpace(fact.Description))
            {
                queryParts.Add(fact.Description);
            }
        }

        var query = string.Join(" ", queryParts);

        try
        {
            // Generate query embedding
            var queryEmbedding = await Ai.Embed(query, ct).ConfigureAwait(false);

            // Search vector store
            var searchOptions = new VectorQueryOptions(
                Query: queryEmbedding,
                TopK: topK,
                Filter: new { sourceDocumentId = document.Id } // Only search this document's passages
            );

            var results = await VectorWorkflow<Passage>.Query(
                searchOptions,
                MeridianConstants.VectorProfile,
                ct).ConfigureAwait(false);

            if (results.Matches.Count == 0)
            {
                _logger.LogDebug("Vector search returned no passages for batch '{Batch}' in document {DocumentId}",
                    batch.CategoryName, document.Id);
                return new List<Passage>();
            }

            // Load passage entities
            var passages = new List<Passage>();
            foreach (var match in results.Matches)
            {
                var passage = await Passage.Get(match.Id, ct).ConfigureAwait(false);
                if (passage != null)
                {
                    passages.Add(passage);
                }
            }

            _logger.LogDebug("Retrieved {PassageCount} relevant passages for batch '{Batch}' from document {DocumentId}",
                passages.Count, batch.CategoryName, document.Id);

            return passages.OrderBy(p => p.SequenceNumber).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve passages for batch '{Batch}' from document {DocumentId}",
                batch.CategoryName, document.Id);
            return new List<Passage>();
        }
    }

    /// <summary>
    /// Expand passages with surrounding context (for dialogue documents).
    /// Adds passages before and after each retrieved passage.
    /// </summary>
    private async Task<List<Passage>> ExpandPassageContextAsync(
        List<Passage> keyPassages,
        int windowSize,
        CancellationToken ct)
    {
        if (keyPassages.Count == 0 || windowSize <= 0)
        {
            return keyPassages;
        }

        var expandedIds = new HashSet<string>();

        foreach (var passage in keyPassages)
        {
            // Add the key passage itself
            expandedIds.Add(passage.Id);

            // Add passages before and after (by sequence number)
            var contextPassages = await Passage.Query(
                p => p.SourceDocumentId == passage.SourceDocumentId &&
                     p.SequenceNumber >= passage.SequenceNumber - windowSize &&
                     p.SequenceNumber <= passage.SequenceNumber + windowSize,
                ct).ConfigureAwait(false);

            foreach (var ctx in contextPassages)
            {
                expandedIds.Add(ctx.Id);
            }
        }

        // Retrieve all and sort chronologically
        var allPassages = new List<Passage>();
        foreach (var id in expandedIds)
        {
            var passage = await Passage.Get(id, ct).ConfigureAwait(false);
            if (passage != null)
            {
                allPassages.Add(passage);
            }
        }

        _logger.LogDebug("Expanded {OriginalCount} passages to {ExpandedCount} with context window {WindowSize}",
            keyPassages.Count, allPassages.Count, windowSize);

        return allPassages.OrderBy(p => p.SequenceNumber).ToList();
    }

    /// <summary>
    /// Build extraction context from passages.
    /// Preserves chronological order and adds passage markers.
    /// </summary>
    private string BuildContextFromPassages(List<Passage> passages)
    {
        if (passages.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (var passage in passages.OrderBy(p => p.SequenceNumber))
        {
            if (passage.PageNumber.HasValue)
            {
                sb.AppendLine($"[Passage {passage.SequenceNumber}, Page {passage.PageNumber}]");
            }
            else
            {
                sb.AppendLine($"[Passage {passage.SequenceNumber}]");
            }

            sb.AppendLine(passage.Text);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
