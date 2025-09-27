using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Koan.Data.Vector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.Infrastructure;

public sealed class DocumentAnalysisPipeline : BackgroundService
{
    private readonly IDocumentPipelineQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DocMindOptions _options;
    private readonly ILogger<DocumentAnalysisPipeline> _logger;

    public DocumentAnalysisPipeline(
        IDocumentPipelineQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<DocMindOptions> options,
        ILogger<DocumentAnalysisPipeline> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = Math.Max(1, _options.Processing.MaxConcurrency);
        using var semaphore = new SemaphoreSlim(concurrency);
        var running = new ConcurrentBag<Task>();

        await foreach (var work in _queue.DequeueAsync(stoppingToken))
        {
            await semaphore.WaitAsync(stoppingToken).ConfigureAwait(false);
            var task = ProcessWorkAsync(work, semaphore, stoppingToken);
            running.Add(task);
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    private Task ProcessWorkAsync(DocumentWorkItem workItem, SemaphoreSlim semaphore, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                var eventSink = services.GetRequiredService<IDocumentProcessingEventSink>();
                var extraction = services.GetRequiredService<ITextExtractionService>();
                var embeddingGenerator = services.GetRequiredService<IEmbeddingGenerator>();
                var vision = services.GetRequiredService<IVisionInsightService>();
                var insightSynthesis = services.GetRequiredService<IInsightSynthesisService>();
                var templateSuggestions = services.GetRequiredService<ITemplateSuggestionService>();
                var clock = services.GetRequiredService<TimeProvider>();
                var logger = services.GetRequiredService<ILogger<DocumentAnalysisPipeline>>();

                var document = await SourceDocument.Get(workItem.DocumentId.ToString(), cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    _logger.LogWarning("Work item {WorkId} references missing document {DocumentId}", workItem.WorkId, workItem.DocumentId);
                    await _queue.CompleteAsync(workItem, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var documentId = Guid.Parse(document.Id);

                await eventSink.RecordAsync(
                    new DocumentProcessingEventEntry(
                        documentId,
                        workItem.Stage,
                        DocumentProcessingStatus.Extracting,
                        Detail: "Starting text extraction",
                        Attempt: workItem.Attempt,
                        CorrelationId: workItem.CorrelationId),
                    cancellationToken).ConfigureAwait(false);

                document.Status = DocumentProcessingStatus.Extracting;
                await document.Save(cancellationToken).ConfigureAwait(false);

                var extractionResult = await extraction.ExtractAsync(document, cancellationToken).ConfigureAwait(false);

                var chunkRefs = new List<ChunkReference>();
                var persistedChunks = new List<DocumentChunk>();
                var chunkIndex = 0;
                foreach (var chunk in extractionResult.Chunks)
                {
                    var entity = new DocumentChunk
                    {
                        SourceDocumentId = documentId,
                        Order = chunk.Index,
                        Text = chunk.Content.Trim(),
                        CharacterCount = chunk.Content.Length,
                        TokenCount = EstimateTokens(chunk.Content),
                        IsLastChunk = chunkIndex == extractionResult.Chunks.Count - 1,
                        StructuredPayload = new Dictionary<string, object?>(chunk.Metadata),
                        ConfidenceByExtractor = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    };

                    entity = await entity.Save(cancellationToken).ConfigureAwait(false);
                    persistedChunks.Add(entity);
                    chunkRefs.Add(new ChunkReference
                    {
                        ChunkId = Guid.Parse(entity.Id),
                        Order = entity.Order
                    });

                    if (Vector<DocumentChunkEmbedding>.IsAvailable)
                    {
                        var embedding = await embeddingGenerator.GenerateAsync(entity.Text, cancellationToken).ConfigureAwait(false);
                        if (embedding is not null && embedding.Length > 0)
                        {
                            await DocumentChunkEmbeddingWriter.UpsertAsync(Guid.Parse(entity.Id), documentId, embedding, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    chunkIndex++;
                }

                document.Summary.TextExtracted = true;
                document.Summary.ContainsImages = extractionResult.ContainsImages;
                document.Summary.ChunkRefs = new List<ChunkReference>(chunkRefs);
                document.Summary.LastCompletedStage = DocumentProcessingStage.GenerateChunks;
                document.Summary.LastKnownStatus = DocumentProcessingStatus.Extracted;
                document.Summary.LastStageCompletedAt = clock.GetUtcNow();
                document.LastProcessedAt = clock.GetUtcNow();
                document.Status = DocumentProcessingStatus.Extracted;
                document.Summary.PrimaryFindings = extractionResult.Text.Length > 320
                    ? extractionResult.Text[..320]
                    : extractionResult.Text;
                await document.Save(cancellationToken).ConfigureAwait(false);

                var extractionMetrics = new Dictionary<string, double>
                {
                    ["chunkCount"] = chunkRefs.Count,
                    ["wordCount"] = extractionResult.WordCount,
                    ["pageCount"] = extractionResult.PageCount,
                    ["containsImages"] = extractionResult.ContainsImages ? 1 : 0
                };
                var extractionContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                DocumentAnalysisDiagnostics.ApplyDiagnostics(extractionMetrics, extractionContext, extractionResult.Diagnostics, "extract");

                await eventSink.RecordAsync(
                    new DocumentProcessingEventEntry(
                        documentId,
                        DocumentProcessingStage.GenerateChunks,
                        DocumentProcessingStatus.Extracted,
                        Detail: $"Generated {chunkRefs.Count} chunks",
                        Context: extractionContext,
                        Metrics: extractionMetrics,
                        Attempt: workItem.Attempt,
                        CorrelationId: workItem.CorrelationId),
                    cancellationToken).ConfigureAwait(false);

                var insightRefs = new List<InsightReference>();

                if (_options.Processing.EnableVisionExtraction && extractionResult.ContainsImages)
                {
                    var visionResult = await vision.TryExtractAsync(document, cancellationToken).ConfigureAwait(false);
                    if (visionResult is not null && !string.IsNullOrWhiteSpace(visionResult.Narrative))
                    {
                        var visionMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["source"] = "vision"
                        };
                        if (!string.IsNullOrWhiteSpace(visionResult.Model))
                        {
                            visionMetadata["model"] = visionResult.Model!;
                        }

                        var insight = new DocumentInsight
                        {
                            SourceDocumentId = documentId,
                            Channel = InsightChannel.Vision,
                            Heading = "Vision summary",
                            Body = visionResult.Narrative,
                            Confidence = visionResult.Confidence ?? visionResult.Observations.FirstOrDefault()?.Confidence,
                            StructuredPayload = new Dictionary<string, object?>(visionResult.StructuredPayload),
                            Metadata = visionMetadata
                        };

                        insight = await insight.Save(cancellationToken).ConfigureAwait(false);
                        document.Summary.VisionExtracted = true;
                        if (Guid.TryParse(insight.Id, out var visionInsightId))
                        {
                            insightRefs.Add(new InsightReference
                            {
                                InsightId = visionInsightId,
                                Channel = InsightChannel.Vision,
                                Confidence = insight.Confidence,
                                Heading = insight.Heading
                            });
                        }

                        foreach (var observation in visionResult.Observations)
                        {
                            var observationInsight = new DocumentInsight
                            {
                                SourceDocumentId = documentId,
                                Channel = InsightChannel.Vision,
                                Heading = $"Vision: {observation.Label}",
                                Body = observation.Summary ?? visionResult.Narrative,
                                Confidence = observation.Confidence,
                                Section = $"vision/{observation.Label.ToLowerInvariant()}",
                                StructuredPayload = new Dictionary<string, object?>(observation.Metadata),
                                Metadata = new Dictionary<string, string>(visionMetadata)
                            };

                            var savedObservation = await observationInsight.Save(cancellationToken).ConfigureAwait(false);
                            if (Guid.TryParse(savedObservation.Id, out var observationId))
                            {
                                insightRefs.Add(new InsightReference
                                {
                                    InsightId = observationId,
                                    Channel = InsightChannel.Vision,
                                    Confidence = savedObservation.Confidence,
                                    Heading = savedObservation.Heading
                                });
                            }
                        }

                        var visionMetrics = new Dictionary<string, double>();
                        foreach (var hint in visionResult.FieldHints)
                        {
                            visionMetrics[$"vision.hint.{hint.Key}"] = hint.Value;
                        }

                        var visionContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        DocumentAnalysisDiagnostics.ApplyDiagnostics(visionMetrics, visionContext, visionResult.Diagnostics, "vision");
                        if (visionResult.StructuredPayload.Count > 0)
                        {
                            visionContext["vision.structured"] = JsonSerializer.Serialize(visionResult.StructuredPayload);
                        }
                        if (!string.IsNullOrWhiteSpace(visionResult.ExtractedText))
                        {
                            visionContext["vision.ocrText"] = visionResult.ExtractedText.Length > 200
                                ? visionResult.ExtractedText[..200] + "…"
                                : visionResult.ExtractedText;
                        }

                        await eventSink.RecordAsync(
                            new DocumentProcessingEventEntry(
                                documentId,
                                DocumentProcessingStage.ExtractVision,
                                DocumentProcessingStatus.Analyzing,
                                Detail: "Vision insights captured",
                                Context: visionContext,
                                Metrics: visionMetrics,
                                Attempt: workItem.Attempt,
                                CorrelationId: workItem.CorrelationId),
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                var textInsights = await insightSynthesis.GenerateAsync(document, extractionResult, persistedChunks, cancellationToken).ConfigureAwait(false);
                string? summaryFromInsights = null;
                foreach (var insight in textInsights)
                {
                    var saved = await insight.Save(cancellationToken).ConfigureAwait(false);
                    if (summaryFromInsights is null && string.Equals(saved.Section, "summary", StringComparison.OrdinalIgnoreCase))
                    {
                        summaryFromInsights = saved.Body;
                    }
                    if (Guid.TryParse(saved.Id, out var savedId))
                    {
                        insightRefs.Add(new InsightReference
                        {
                            InsightId = savedId,
                            Channel = saved.Channel,
                            Confidence = saved.Confidence,
                            Heading = saved.Heading
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(summaryFromInsights))
                {
                    document.Summary.PrimaryFindings = summaryFromInsights;
                }

                document.Summary.InsightRefs = new List<InsightReference>(insightRefs);
                document.Summary.LastCompletedStage = DocumentProcessingStage.GenerateInsights;
                document.Summary.LastKnownStatus = DocumentProcessingStatus.InsightsReady;
                document.Summary.LastStageCompletedAt = clock.GetUtcNow();
                document.Status = DocumentProcessingStatus.InsightsReady;
                document.LastProcessedAt = clock.GetUtcNow();
                await document.Save(cancellationToken).ConfigureAwait(false);

                await eventSink.RecordAsync(
                    new DocumentProcessingEventEntry(
                        documentId,
                        DocumentProcessingStage.GenerateInsights,
                        DocumentProcessingStatus.InsightsReady,
                        Detail: $"Persisted {insightRefs.Count} insights",
                        Context: string.IsNullOrWhiteSpace(summaryFromInsights)
                            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["insights.summary"] = summaryFromInsights!.Length > 200
                                    ? summaryFromInsights[..200] + "…"
                                    : summaryFromInsights
                            },
                        Metrics: new Dictionary<string, double>
                        {
                            ["insights.count"] = insightRefs.Count,
                            ["insights.summaryLength"] = summaryFromInsights?.Length ?? 0
                        },
                        Attempt: workItem.Attempt,
                        CorrelationId: workItem.CorrelationId),
                    cancellationToken).ConfigureAwait(false);

                var suggestions = await templateSuggestions.SuggestAsync(document, persistedChunks, cancellationToken).ConfigureAwait(false);
                if (suggestions.Count > 0)
                {
                    document.Summary.AutoClassificationConfidence = suggestions.Max(s => s.Confidence);
                    var topSuggestion = suggestions[0];

                    if (topSuggestion.AutoAccepted && string.IsNullOrWhiteSpace(document.AssignedProfileId))
                    {
                        document.AssignedProfileId = topSuggestion.ProfileId;
                        document.AssignedBySystem = true;
                    }

                    var suggestionMetrics = new Dictionary<string, double>
                    {
                        ["suggestions.count"] = suggestions.Count,
                        ["suggestions.bestConfidence"] = topSuggestion.Confidence
                    };

                    var suggestionContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["suggestions.bestProfileId"] = topSuggestion.ProfileId,
                        ["suggestions.autoAccepted"] = topSuggestion.AutoAccepted.ToString()
                    };

                    foreach (var kvp in topSuggestion.Diagnostics)
                    {
                        suggestionContext[$"diagnostics.{kvp.Key}"] = kvp.Value;
                    }

                    await eventSink.RecordAsync(
                        new DocumentProcessingEventEntry(
                            documentId,
                            DocumentProcessingStage.Aggregate,
                            DocumentProcessingStatus.Analyzing,
                            Detail: "Template suggestions evaluated",
                            Context: suggestionContext,
                            Metrics: suggestionMetrics,
                            Attempt: workItem.Attempt,
                            CorrelationId: workItem.CorrelationId),
                        cancellationToken).ConfigureAwait(false);
                }

                document.Status = DocumentProcessingStatus.Completed;
                document.LastProcessedAt = clock.GetUtcNow();
                document.Summary.LastCompletedStage = DocumentProcessingStage.Complete;
                document.Summary.LastKnownStatus = DocumentProcessingStatus.Completed;
                document.Summary.LastStageCompletedAt = clock.GetUtcNow();
                await document.Save(cancellationToken).ConfigureAwait(false);

                await eventSink.RecordAsync(
                    new DocumentProcessingEventEntry(
                        documentId,
                        DocumentProcessingStage.Complete,
                        DocumentProcessingStatus.Completed,
                        Detail: "Document processing completed",
                        Attempt: workItem.Attempt,
                        CorrelationId: workItem.CorrelationId,
                        IsTerminal = true),
                    cancellationToken).ConfigureAwait(false);

                workItem.UpdateStage(DocumentProcessingStage.Complete, DocumentProcessingStatus.Completed);
                workItem.MarkCompleted(DocumentProcessingStatus.Completed);
                await _queue.CompleteAsync(workItem, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline failed for document {DocumentId}", workItem.DocumentId);
                await HandleFailureAsync(workItem, ex, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        }, cancellationToken);

    private async Task HandleFailureAsync(DocumentWorkItem workItem, Exception exception, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var eventSink = scope.ServiceProvider.GetRequiredService<IDocumentProcessingEventSink>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var document = await SourceDocument.Get(workItem.DocumentId.ToString(), cancellationToken).ConfigureAwait(false);
        if (document is not null)
        {
            var documentId = Guid.Parse(document.Id);
            document.Status = DocumentProcessingStatus.Failed;
            document.LastError = exception.Message;
            document.LastProcessedAt = clock.GetUtcNow();
            document.Summary.LastKnownStatus = DocumentProcessingStatus.Failed;
            document.Summary.LastCompletedStage = workItem.Stage;
            document.Summary.LastStageCompletedAt = clock.GetUtcNow();
            await document.Save(cancellationToken).ConfigureAwait(false);

            await eventSink.RecordAsync(
                new DocumentProcessingEventEntry(
                    documentId,
                    DocumentProcessingStage.Failed,
                    DocumentProcessingStatus.Failed,
                    Detail: "Processing failed",
                    Error: exception.Message,
                    Attempt: workItem.Attempt,
                    CorrelationId: workItem.CorrelationId),
                cancellationToken).ConfigureAwait(false);

            workItem.UpdateStage(workItem.Stage, DocumentProcessingStatus.Failed);
            workItem.MarkCompleted(DocumentProcessingStatus.Failed);
            var scheduled = await _queue.ScheduleRetryAsync(workItem, exception, cancellationToken).ConfigureAwait(false);
            if (!scheduled)
            {
                await eventSink.RecordAsync(
                    new DocumentProcessingEventEntry(
                        documentId,
                        DocumentProcessingStage.Failed,
                        DocumentProcessingStatus.Failed,
                        Detail: "Retry limit reached",
                        Error: exception.Message,
                        Attempt: workItem.Attempt,
                        CorrelationId: workItem.CorrelationId,
                        IsTerminal = true),
                    cancellationToken).ConfigureAwait(false);
                await _queue.CompleteAsync(workItem, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            workItem.UpdateStage(workItem.Stage, DocumentProcessingStatus.Failed);
            workItem.MarkCompleted(DocumentProcessingStatus.Failed);
            var scheduled = await _queue.ScheduleRetryAsync(workItem, exception, cancellationToken).ConfigureAwait(false);
            if (!scheduled)
            {
                await _queue.CompleteAsync(workItem, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static int EstimateTokens(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        return Math.Max(1, content.Length / 4);
    }
}

internal static class DocumentChunkEmbeddingWriter
{
    public static async Task UpsertAsync(Guid chunkId, Guid documentId, float[] embedding, CancellationToken cancellationToken)
    {
        if (!Vector<DocumentChunkEmbedding>.IsAvailable || embedding is null || embedding.Length == 0)
        {
            return;
        }

        var entity = new DocumentChunkEmbedding
        {
            DocumentChunkId = chunkId,
            SourceDocumentId = documentId,
            Embedding = embedding,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        await entity.Save(cancellationToken).ConfigureAwait(false);
    }
}

internal static class DocumentAnalysisDiagnostics
{
    public static void ApplyDiagnostics(IDictionary<string, double> metrics, IDictionary<string, string> context, IReadOnlyDictionary<string, object?> diagnostics, string prefix)
    {
        if (diagnostics is null)
        {
            return;
        }

        foreach (var kvp in diagnostics)
        {
            if (kvp.Value is null)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
            switch (kvp.Value)
            {
                case double d:
                    metrics[name] = d;
                    break;
                case float f:
                    metrics[name] = f;
                    break;
                case int i:
                    metrics[name] = i;
                    break;
                case long l:
                    metrics[name] = l;
                    break;
                case bool b:
                    metrics[name] = b ? 1 : 0;
                    break;
                case string s when !string.IsNullOrWhiteSpace(s):
                    context[name] = s;
                    break;
                default:
                    context[name] = JsonSerializer.Serialize(kvp.Value);
                    break;
            }
        }
    }
}
