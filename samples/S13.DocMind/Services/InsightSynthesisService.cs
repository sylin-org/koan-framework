using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

    public async Task<ManualAnalysisSynthesisResult> GenerateManualSessionAsync(
        ManualAnalysisSession session,
        SemanticTypeProfile? profile,
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (documents is null) throw new ArgumentNullException(nameof(documents));
        if (documents.Count == 0)
        {
            throw new ValidationException("At least one document is required for manual analysis.");
        }

        var limitedDocuments = documents
            .Take(Math.Max(1, _options.Manual.MaxDocuments))
            .ToList();

        if (limitedDocuments.Count < documents.Count)
        {
            _logger.LogInformation(
                "Manual session {SessionId} limited to {Limit} documents (requested {Requested})",
                session.Id,
                _options.Manual.MaxDocuments,
                documents.Count);
        }

        var snapshots = await BuildManualSnapshotsAsync(session, limitedDocuments, cancellationToken).ConfigureAwait(false);
        var averageConfidence = snapshots.Count > 0
            ? snapshots.Average(snapshot => snapshot.Confidence)
            : _options.Manual.DefaultConfidence;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = "fallback",
            ["documents.count"] = snapshots.Count.ToString(CultureInfo.InvariantCulture),
            ["confidence.avg"] = averageConfidence.ToString("0.000", CultureInfo.InvariantCulture)
        };

        if (profile is not null)
        {
            metadata["profileId"] = profile.Id;
            metadata["profileName"] = profile.Name;
        }

        AiChatResponse? response = null;
        var usedAi = _ai is not null && _options.Manual.EnableSessions;

        if (usedAi)
        {
            try
            {
                var request = new AiChatRequest
                {
                    Model = _options.Ai.DefaultModel,
                    Messages =
                    {
                        new AiMessage("system", BuildManualSystemPrompt(session, profile, snapshots)),
                        new AiMessage("user", BuildManualUserPrompt(session, profile, snapshots))
                    }
                };

                response = await _ai!.PromptAsync(request, cancellationToken).ConfigureAwait(false);
                usedAi = !string.IsNullOrWhiteSpace(response.Text);
            }
            catch (Exception ex)
            {
                usedAi = false;
                metadata["aiError"] = ex.GetType().Name;
                _logger.LogWarning(ex, "Manual synthesis failed for session {SessionId}", session.Id);
            }
        }

        if (usedAi && response is not null)
        {
            metadata["mode"] = "ai";
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

        var synthesis = BuildManualSynthesis(session, profile, snapshots, response, usedAi, averageConfidence, metadata);
        var telemetry = BuildManualRunTelemetry(session, snapshots, response, synthesis);

        return new ManualAnalysisSynthesisResult(synthesis, telemetry);
    }

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

    private async Task<List<ManualDocumentSnapshot>> BuildManualSnapshotsAsync(
        ManualAnalysisSession session,
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<ManualDocumentSnapshot>();

        foreach (var document in documents)
        {
            if (!Guid.TryParse(document.Id, out var documentId))
            {
                continue;
            }

            var sessionDocument = session.Documents.FirstOrDefault(d => d.SourceDocumentId == documentId);
            var insights = await DocumentInsight
                .Query($"SourceDocumentId == '{documentId}'", cancellationToken)
                .ConfigureAwait(false);
            var orderedInsights = insights
                .OrderByDescending(i => i.Confidence ?? _options.Manual.DefaultConfidence)
                .ToList();

            var confidence = orderedInsights.Count > 0
                ? orderedInsights.Where(i => i.Confidence.HasValue).Select(i => i.Confidence!.Value).DefaultIfEmpty(_options.Manual.DefaultConfidence).Average()
                : _options.Manual.DefaultConfidence;

            var summary = !string.IsNullOrWhiteSpace(sessionDocument?.Notes)
                ? sessionDocument!.Notes!
                : !string.IsNullOrWhiteSpace(document.Summary.PrimaryFindings)
                    ? document.Summary.PrimaryFindings!
                    : orderedInsights.FirstOrDefault()?.Body ?? string.Empty;

            var topics = ExtractTopics(orderedInsights, document);

            snapshots.Add(new ManualDocumentSnapshot(document, sessionDocument, orderedInsights, confidence, summary, topics));
        }

        return snapshots;
    }

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

    private static string BuildManualSystemPrompt(ManualAnalysisSession session, SemanticTypeProfile? profile, IReadOnlyList<ManualDocumentSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are DocMind, an analyst consolidating structured findings across multiple documents.");
        builder.AppendLine("Use DOC_## citations (e.g. DOC_01) whenever referencing a source document.");
        builder.AppendLine("Return only the requested delimited sections.");

        if (profile is not null && !string.IsNullOrWhiteSpace(profile.Prompt.SystemPrompt))
        {
            builder.AppendLine();
            builder.AppendLine("Template guidance:");
            builder.AppendLine(profile.Prompt.SystemPrompt);
        }

        if (!string.IsNullOrWhiteSpace(session.Prompt.Instructions))
        {
            builder.AppendLine();
            builder.AppendLine("Operator instructions:");
            builder.AppendLine(session.Prompt.Instructions);
        }

        builder.AppendLine();
        builder.AppendLine($"Documents provided: {snapshots.Count}");
        return builder.ToString();
    }

    private string BuildManualUserPrompt(ManualAnalysisSession session, SemanticTypeProfile? profile, IReadOnlyList<ManualDocumentSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== SESSION CONTEXT ===");
        builder.AppendLine($"Title: {session.Title}");
        if (!string.IsNullOrWhiteSpace(session.Description))
        {
            builder.AppendLine($"Description: {session.Description}");
        }
        builder.AppendLine($"Documents selected: {snapshots.Count}");
        builder.AppendLine();

        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            var alias = $"DOC_{i + 1:00}";
            builder.AppendLine($"=== {alias} | {snapshot.Document.DisplayName ?? snapshot.Document.FileName} ===");
            builder.AppendLine($"Status: {snapshot.Document.Status}");
            builder.AppendLine($"Confidence: {snapshot.Confidence:0.000}");
            if (snapshot.SessionDocument is not null && !string.IsNullOrWhiteSpace(snapshot.SessionDocument.Notes))
            {
                builder.AppendLine($"Operator notes: {snapshot.SessionDocument.Notes}");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.Summary))
            {
                builder.AppendLine($"Summary: {snapshot.Summary}");
            }
            if (snapshot.Topics.Count > 0)
            {
                builder.AppendLine($"Topics: {string.Join(", ", snapshot.Topics)}");
            }

            var topInsight = snapshot.Insights.FirstOrDefault();
            if (topInsight is not null)
            {
                builder.AppendLine($"Top insight: {topInsight.Heading} -> {topInsight.Body}");
            }

            builder.AppendLine();
        }

        var consolidatedTopics = snapshots
            .SelectMany(snapshot => snapshot.Topics)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (consolidatedTopics.Count > 0)
        {
            builder.AppendLine("=== CONSOLIDATED TOPICS ===");
            builder.AppendLine(string.Join(", ", consolidatedTopics));
            builder.AppendLine();
        }

        var findings = BuildManualFindings(snapshots);
        if (findings.Count > 0)
        {
            builder.AppendLine("=== CONSOLIDATED KEY FACTS ===");
            foreach (var finding in findings.Take(20))
            {
                var source = finding.Sources.FirstOrDefault() ?? "DOC_??";
                var confidence = finding.Confidence ?? _options.Manual.DefaultConfidence;
                builder.AppendLine($"- {finding.Body} (from {source}, confidence: {confidence:0.000})");
            }
            builder.AppendLine();
        }

        if (profile is not null)
        {
            builder.AppendLine("=== TEMPLATE MARKUP ===");
            builder.AppendLine(profile.Prompt.UserTemplate);
            builder.AppendLine();
        }

        if (session.Prompt.Variables.Count > 0)
        {
            builder.AppendLine("=== VARIABLE OVERRIDES ===");
            foreach (var (key, value) in session.Prompt.Variables)
            {
                builder.AppendLine($"{key}: {value}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("=== OUTPUT REQUIREMENTS ===");
        builder.AppendLine("Return ONLY these blocks in order:");
        builder.AppendLine("---FILLED_DOCUMENT_TYPE_START---");
        builder.AppendLine("(filled template using consolidated findings with DOC_## citations)");
        builder.AppendLine("---FILLED_DOCUMENT_TYPE_END---");
        builder.AppendLine("---CONTEXT_UNDERSTANDING_START---");
        builder.AppendLine("(brief synthesis of approach, sources used, and confidence)");
        builder.AppendLine("---CONTEXT_UNDERSTANDING_END---");

        return builder.ToString();
    }

    private ManualAnalysisSynthesis BuildManualSynthesis(
        ManualAnalysisSession session,
        SemanticTypeProfile? profile,
        IReadOnlyList<ManualDocumentSnapshot> snapshots,
        AiChatResponse? response,
        bool usedAi,
        double averageConfidence,
        Dictionary<string, string> metadata)
    {
        var filled = usedAi ? ExtractDelimitedContent(response?.Text, "---FILLED_DOCUMENT_TYPE_START---", "---FILLED_DOCUMENT_TYPE_END---") : string.Empty;
        var context = usedAi ? ExtractDelimitedContent(response?.Text, "---CONTEXT_UNDERSTANDING_START---", "---CONTEXT_UNDERSTANDING_END---") : string.Empty;

        if (string.IsNullOrWhiteSpace(filled))
        {
            filled = BuildFallbackFilledTemplate(profile, snapshots);
        }

        if (string.IsNullOrWhiteSpace(context))
        {
            context = BuildFallbackContext(session, snapshots, usedAi);
        }

        var findings = BuildManualFindings(snapshots);

        return new ManualAnalysisSynthesis
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Confidence = averageConfidence,
            FilledTemplate = filled,
            ContextSummary = context,
            Metadata = metadata,
            Findings = findings
        };
    }

    private ManualAnalysisRun BuildManualRunTelemetry(
        ManualAnalysisSession session,
        IReadOnlyList<ManualDocumentSnapshot> snapshots,
        AiChatResponse? response,
        ManualAnalysisSynthesis synthesis)
    {
        var run = new ManualAnalysisRun
        {
            ExecutedAt = DateTimeOffset.UtcNow,
            Model = !string.IsNullOrWhiteSpace(response?.Model) ? response.Model! : _options.Ai.DefaultModel,
            TokensIn = response?.TokensIn,
            TokensOut = response?.TokensOut,
            Confidence = synthesis.Confidence,
            DocumentIds = snapshots
                .Select(snapshot => Guid.TryParse(snapshot.Document.Id, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList()
        };

        foreach (var (key, value) in synthesis.Metadata)
        {
            run.Metadata[key] = value;
        }

        run.Metadata["sessionId"] = session.Id;

        return run;
    }

    private static List<string> ExtractTopics(IReadOnlyList<DocumentInsight> insights, SourceDocument document)
    {
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var insight in insights)
        {
            if (insight.StructuredPayload is null)
            {
                continue;
            }

            if (insight.StructuredPayload.TryGetValue("tags", out var value))
            {
                switch (value)
                {
                    case JsonElement json when json.ValueKind == JsonValueKind.Array:
                        foreach (var element in json.EnumerateArray())
                        {
                            var tag = element.GetString();
                            if (!string.IsNullOrWhiteSpace(tag))
                            {
                                topics.Add(tag.Trim());
                            }
                        }
                        break;
                    case IEnumerable<object?> enumerable:
                        foreach (var entry in enumerable)
                        {
                            if (entry is string str && !string.IsNullOrWhiteSpace(str))
                            {
                                topics.Add(str.Trim());
                            }
                        }
                        break;
                    case string str when !string.IsNullOrWhiteSpace(str):
                        foreach (var part in str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            topics.Add(part);
                        }
                        break;
                }
            }
        }

        foreach (var tag in document.Tags.Keys)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                topics.Add(tag.Trim());
            }
        }

        foreach (var tag in document.Tags.Values)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                topics.Add(tag.Trim());
            }
        }

        return topics.Take(10).ToList();
    }

    private List<ManualAnalysisFinding> BuildManualFindings(IReadOnlyList<ManualDocumentSnapshot> snapshots)
    {
        var findings = new List<ManualAnalysisFinding>();

        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            var alias = $"DOC_{i + 1:00}";
            var highConfidence = snapshot.Insights
                .Where(insight => insight.Confidence.HasValue && insight.Confidence.Value >= 0.6)
                .Take(5)
                .ToList();

            if (highConfidence.Count == 0 && snapshot.Insights.Count > 0)
            {
                highConfidence.Add(snapshot.Insights.First());
            }

            if (highConfidence.Count == 0)
            {
                findings.Add(new ManualAnalysisFinding
                {
                    Title = snapshot.Document.DisplayName ?? snapshot.Document.FileName,
                    Body = snapshot.Summary,
                    Confidence = snapshot.Confidence,
                    Sources = new List<string> { alias },
                    Structured = new Dictionary<string, object?>()
                });
                continue;
            }

            foreach (var insight in highConfidence)
            {
                var structured = insight.StructuredPayload is null
                    ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object?>(insight.StructuredPayload, StringComparer.OrdinalIgnoreCase);

                findings.Add(new ManualAnalysisFinding
                {
                    Title = string.IsNullOrWhiteSpace(insight.Heading) ? "Key finding" : insight.Heading,
                    Body = insight.Body,
                    Confidence = insight.Confidence ?? snapshot.Confidence,
                    Sources = new List<string> { alias },
                    Structured = structured
                });
            }
        }

        return findings;
    }

    private static string BuildFallbackFilledTemplate(SemanticTypeProfile? profile, IReadOnlyList<ManualDocumentSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        if (profile is not null)
        {
            builder.AppendLine($"Template: {profile.Name}");
            builder.AppendLine(new string('-', Math.Max(8, profile.Name.Length + 10)));
        }

        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            var alias = $"DOC_{i + 1:00}";
            builder.AppendLine($"{alias} – {snapshot.Document.DisplayName ?? snapshot.Document.FileName}");
            if (!string.IsNullOrWhiteSpace(snapshot.Summary))
            {
                builder.AppendLine(snapshot.Summary);
            }
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildFallbackContext(ManualAnalysisSession session, IReadOnlyList<ManualDocumentSnapshot> snapshots, bool usedAi)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Session \"{session.Title}\" processed {snapshots.Count} document(s).");
        builder.AppendLine(usedAi
            ? "AI output did not include structured context; using aggregated findings instead."
            : "AI model unavailable, generated manual summary from stored insights.");

        var recent = snapshots.FirstOrDefault();
        if (recent is not null && !string.IsNullOrWhiteSpace(recent.Summary))
        {
            builder.AppendLine($"Representative summary: {recent.Summary}");
        }

        return builder.ToString().Trim();
    }

    private static string ExtractDelimitedContent(string? text, string startDelimiter, string endDelimiter)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var startIndex = text.IndexOf(startDelimiter, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        startIndex += startDelimiter.Length;
        var endIndex = text.IndexOf(endDelimiter, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return string.Empty;
        }

        return text[startIndex..endIndex].Trim();
    }

    private sealed record ManualDocumentSnapshot(
        SourceDocument Document,
        ManualAnalysisDocument? SessionDocument,
        IReadOnlyList<DocumentInsight> Insights,
        double Confidence,
        string Summary,
        IReadOnlyList<string> Topics);
}
