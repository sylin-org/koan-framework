using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed class InsightSynthesisService : IInsightSynthesisService
{
    private readonly IAi? _ai;
    private readonly DocMindOptions _options;
    private readonly ILogger<InsightSynthesisService> _logger;

    public InsightSynthesisService(IServiceProvider serviceProvider, IOptions<DocMindOptions> options, ILogger<InsightSynthesisService> logger)
    {
        _ai = serviceProvider.GetService<IAi>();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InsightSynthesisResult> GenerateAsync(SourceDocument document, DocumentExtractionResult extraction, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var insights = new List<DocumentInsight>();
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["chunks.total"] = chunks.Count
        };
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var documentId = Guid.Parse(document.Id);
        AiChatResponse? response = null;
        var usedAi = false;

        if (_ai is not null && !string.IsNullOrWhiteSpace(extraction.Text))
        {
            try
            {
                var prompt = BuildPrompt(document, extraction);
                response = await _ai.PromptAsync(new AiChatRequest
                {
                    Model = _options.Ai.DefaultModel,
                    Messages =
                    {
                        new AiMessage("system", "You are DocMind, an analyst producing structured findings."),
                        new AiMessage("user", prompt)
                    }
                }, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    var aiInsights = ParseAiInsights(documentId, chunks, response);
                    if (aiInsights.Count > 0)
                    {
                        insights.AddRange(aiInsights);
                        metrics["insights.ai"] = aiInsights.Count;
                        usedAi = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI insight synthesis failed for document {DocumentId}", document.Id);
                context["aiError"] = ex.GetType().Name;
            }
        }

        if (!usedAi)
        {
            context["mode"] = "fallback";
        }

        if (insights.Count == 0)
        {
            var sb = new StringBuilder();
            foreach (var chunk in chunks.Take(3))
            {
                var text = chunk.Text ?? string.Empty;
                var snippet = text.Length > 160 ? text[..160] + "…" : text;
                sb.AppendLine(snippet.Trim());
                sb.AppendLine();
            }

            var fallback = sb.Length == 0
                ? extraction.Text[..Math.Min(320, extraction.Text.Length)]
                : sb.ToString();
            var metadata = BuildMetadata(response, "fallback");
            insights.Add(new DocumentInsight
            {
                SourceDocumentId = documentId,
                Channel = InsightChannel.Text,
                Heading = "Auto-generated overview",
                Body = fallback.Trim(),
                Confidence = 0.4,
                Section = "summary",
                StructuredPayload = new Dictionary<string, object?>
                {
                    ["type"] = "summary",
                    ["source"] = usedAi ? "ai" : "fallback"
                },
                Metadata = metadata
            });
            metrics["insights.fallback"] = 1;
        }
        else
        {
            context["mode"] = usedAi ? "ai" : context.GetValueOrDefault("mode", "fallback");
        }

        foreach (var chunk in chunks)
        {
            var chunkId = Guid.Parse(chunk.Id);
            var metadata = BuildMetadata(response, "chunk-highlight");
            metadata["chunkOrder"] = chunk.Order.ToString(CultureInfo.InvariantCulture);
            insights.Add(new DocumentInsight
            {
                SourceDocumentId = documentId,
                ChunkId = chunkId,
                Channel = InsightChannel.Text,
                Heading = $"Chunk {chunk.Order + 1} highlight",
                Body = chunk.Text.Length > 200 ? chunk.Text[..200] + "…" : chunk.Text,
                Confidence = 0.3,
                Section = "chunk",
                StructuredPayload = new Dictionary<string, object?>
                {
                    ["chunkOrder"] = chunk.Order,
                    ["characterCount"] = chunk.CharacterCount,
                    ["tokenCount"] = chunk.TokenCount
                },
                Metadata = metadata
            });
        }

        if (chunks.Count > 0)
        {
            metrics["insights.chunkHighlights"] = chunks.Count;
        }

        metrics["insights.total"] = insights.Count;

        if (response is not null)
        {
            if (!string.IsNullOrWhiteSpace(response.Model))
            {
                context["model"] = response.Model!;
            }

            if (response.TokensIn.HasValue)
            {
                context["tokensIn"] = response.TokensIn.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (response.TokensOut.HasValue)
            {
                context["tokensOut"] = response.TokensOut.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        return new InsightSynthesisResult(
            insights,
            metrics,
            context,
            response?.TokensIn,
            response?.TokensOut);
    }

    private IReadOnlyList<DocumentInsight> ParseAiInsights(Guid documentId, IReadOnlyList<DocumentChunk> chunks, AiChatResponse response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response.Text);
            var root = doc.RootElement;
            var insights = new List<DocumentInsight>();
            var chunkByOrder = chunks.ToDictionary(c => c.Order);
            var metadataBase = BuildMetadata(response, "ai");
            if (root.TryGetProperty("summary", out var summaryElement))
            {
                var summary = summaryElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    insights.Add(CreateInsight(
                        documentId,
                        heading: "AI summary",
                        body: summary!,
                        section: "summary",
                        confidence: ExtractConfidence(root, "summaryConfidence"),
                        metadata: metadataBase,
                        structured: new Dictionary<string, object?>
                        {
                            ["type"] = "summary",
                            ["source"] = "ai"
                        }));
                }
            }

            if (root.TryGetProperty("sections", out var sectionsElement) && sectionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sectionsElement.EnumerateArray())
                {
                    var heading = section.TryGetProperty("heading", out var headingElement)
                        ? headingElement.GetString() ?? "Section insight"
                        : "Section insight";
                    var body = section.TryGetProperty("body", out var bodyElement)
                        ? bodyElement.GetString() ?? string.Empty
                        : string.Empty;
                    var tags = section.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array
                        ? string.Join(',', tagsElement.EnumerateArray().Select(tag => tag.GetString()).Where(t => !string.IsNullOrWhiteSpace(t)))
                        : null;
                    var confidence = section.TryGetProperty("confidence", out var confElement) && confElement.TryGetDouble(out var confValue)
                        ? Math.Clamp(confValue, 0, 1)
                        : (double?)null;

                    var structured = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tags"] = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>()
                    };

                    if (section.TryGetProperty("structured", out var structuredElement) && structuredElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in structuredElement.EnumerateObject())
                        {
                            structured[property.Name] = ConvertJsonValue(property.Value);
                        }
                    }

                    var metadata = BuildMetadata(response, "section");
                    if (!string.IsNullOrWhiteSpace(tags))
                    {
                        metadata["tags"] = tags;
                    }

                    var insight = CreateInsight(documentId, heading, body, "section", confidence, metadata, structured);
                    if (section.TryGetProperty("references", out var referencesElement) && referencesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var reference in referencesElement.EnumerateArray())
                        {
                            if (reference.TryGetInt32(out var order) && chunkByOrder.TryGetValue(order, out var chunk))
                            {
                                insight.ChunkId = Guid.Parse(chunk.Id);
                                break;
                            }
                        }
                    }

                    insights.Add(insight);
                }
            }

            if (root.TryGetProperty("actions", out var actionsElement) && actionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actionsElement.EnumerateArray())
                {
                    var body = action.TryGetProperty("description", out var descriptionElement)
                        ? descriptionElement.GetString() ?? string.Empty
                        : string.Empty;
                    var heading = action.TryGetProperty("title", out var titleElement)
                        ? titleElement.GetString() ?? "Recommended action"
                        : "Recommended action";
                    var confidence = action.TryGetProperty("confidence", out var confElement) && confElement.TryGetDouble(out var confValue)
                        ? Math.Clamp(confValue, 0, 1)
                        : (double?)null;

                    var structured = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (action.TryGetProperty("dueDate", out var dueElement))
                    {
                        structured["dueDate"] = dueElement.GetString();
                    }
                    if (action.TryGetProperty("owner", out var ownerElement))
                    {
                        structured["owner"] = ownerElement.GetString();
                    }

                    var metadata = BuildMetadata(response, "action");
                    insights.Add(CreateInsight(documentId, heading, body, "action", confidence, metadata, structured));
                }
            }

            if (root.TryGetProperty("entities", out var entitiesElement) && entitiesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in entitiesElement.EnumerateArray())
                {
                    var name = entity.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var type = entity.TryGetProperty("type", out var typeElement)
                        ? typeElement.GetString() ?? "entity"
                        : "entity";
                    var confidence = entity.TryGetProperty("confidence", out var confElement) && confElement.TryGetDouble(out var confValue)
                        ? Math.Clamp(confValue, 0, 1)
                        : (double?)null;

                    var structured = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["type"] = type
                    };
                    if (entity.TryGetProperty("structured", out var structuredElement) && structuredElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in structuredElement.EnumerateObject())
                        {
                            structured[property.Name] = ConvertJsonValue(property.Value);
                        }
                    }

                    var metadata = BuildMetadata(response, "entity");
                    metadata["entityType"] = type;
                    insights.Add(CreateInsight(documentId, name, entity.TryGetProperty("summary", out var entitySummaryElement) ? entitySummaryElement.GetString() ?? string.Empty : string.Empty, "entity", confidence, metadata, structured));
                }
            }

            return insights;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unable to parse AI insight response");
            return Array.Empty<DocumentInsight>();
        }
    }

    private static DocumentInsight CreateInsight(
        Guid documentId,
        string heading,
        string body,
        string section,
        double? confidence,
        Dictionary<string, string> metadata,
        Dictionary<string, object?> structured)
        => new()
        {
            SourceDocumentId = documentId,
            Heading = heading,
            Body = body.Trim(),
            Channel = InsightChannel.Text,
            Confidence = confidence,
            Section = section,
            Metadata = metadata,
            StructuredPayload = structured,
            GeneratedAt = DateTimeOffset.UtcNow
        };

    private static double? ExtractConfidence(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var element) && element.TryGetDouble(out var value)
            ? Math.Clamp(value, 0, 1)
            : null;

    private static Dictionary<string, string> BuildMetadata(AiChatResponse? response, string kind)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = kind
        };

        if (response is not null)
        {
            if (!string.IsNullOrWhiteSpace(response.Model))
            {
                metadata["model"] = response.Model!;
            }

            if (response.TokensIn.HasValue)
            {
                metadata["tokensIn"] = response.TokensIn.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (response.TokensOut.HasValue)
            {
                metadata["tokensOut"] = response.TokensOut.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        return metadata;
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value), StringComparer.OrdinalIgnoreCase),
            _ => element.ToString()
        };

    private static string BuildPrompt(SourceDocument document, DocumentExtractionResult extraction)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"You are DocMind, producing structured JSON insights for {document.DisplayName ?? document.FileName}.");
        builder.AppendLine("Return STRICT JSON with fields: summary (string), summaryConfidence (number 0-1), sections (array of { heading, body, tags (array), confidence, references (array chunkOrder), structured (object) }), actions (array of { title, description, dueDate, owner, confidence }), entities (array of { name, type, summary, confidence, structured }).");
        builder.AppendLine("Do not include any prose outside the JSON object.");
        if (!string.IsNullOrWhiteSpace(extraction.Language))
        {
            builder.AppendLine($"Document language: {extraction.Language}.");
        }
        builder.AppendLine("--- DOCUMENT TEXT START ---");
        builder.AppendLine(extraction.Text.Length > 4000 ? extraction.Text[..4000] : extraction.Text);
        builder.AppendLine("--- DOCUMENT TEXT END ---");
        return builder.ToString();
    }

}
