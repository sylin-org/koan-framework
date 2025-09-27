using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Koan.Data.Vector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure.Repositories;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.Infrastructure;

public sealed class DocumentProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DocMindOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DocumentProcessingWorker> _logger;

    public DocumentProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<DocMindOptions> options,
        TimeProvider clock,
        ILogger<DocumentProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.Processing.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            if (!processed)
            {
                try
                {
                    await Task.Delay(pollInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var slice = await DocumentProcessingJobRepository
            .GetPendingAsync(now, _options.Processing.WorkerBatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (slice.Items.Count == 0)
        {
            return false;
        }

        foreach (var job in slice.Items)
        {
            try
            {
                await ProcessJobAsync(job, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Processing job {JobId} failed", job.Id);
            }
        }

        return true;
    }

    private async Task ProcessJobAsync(DocumentProcessingJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var eventSink = services.GetRequiredService<IDocumentProcessingEventSink>();
        var extraction = services.GetRequiredService<ITextExtractionService>();
        var embeddingGenerator = services.GetRequiredService<IEmbeddingGenerator>();
        var vision = services.GetRequiredService<IVisionInsightService>();
        var insightSynthesis = services.GetRequiredService<IInsightSynthesisService>();
        var templateSuggestions = services.GetRequiredService<ITemplateSuggestionService>();

        var document = await SourceDocumentRepository
            .GetAsync(job.SourceDocumentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            await MarkMissingDocumentAsync(job, cancellationToken).ConfigureAwait(false);
            return;
        }

        var session = new ProcessingSession(
            job,
            document,
            eventSink,
            extraction,
            embeddingGenerator,
            vision,
            insightSynthesis,
            templateSuggestions,
            _clock,
            _options,
            _logger);

        await session.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MarkMissingDocumentAsync(DocumentProcessingJob job, CancellationToken cancellationToken)
    {
        job.Status = DocumentProcessingStatus.Cancelled;
        job.LastError = "Source document not found";
        job.UpdatedAt = DateTimeOffset.UtcNow;
        job.CompletedAt = job.UpdatedAt;
        job.NextAttemptAt = null;
        await job.Save(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ProcessingSession
    {
        private readonly DocumentProcessingJob _job;
        private readonly SourceDocument _document;
        private readonly IDocumentProcessingEventSink _eventSink;
        private readonly ITextExtractionService _textExtraction;
        private readonly IEmbeddingGenerator _embeddingGenerator;
        private readonly IVisionInsightService _vision;
        private readonly IInsightSynthesisService _insightSynthesis;
        private readonly ITemplateSuggestionService _templateSuggestions;
        private readonly TimeProvider _clock;
        private readonly DocMindOptions _options;
        private readonly ILogger _outerLogger;

        private DocumentExtractionResult? _extractionResult;
        private IReadOnlyList<DocumentChunk> _persistedChunks = Array.Empty<DocumentChunk>();

        public ProcessingSession(
            DocumentProcessingJob job,
            SourceDocument document,
            IDocumentProcessingEventSink eventSink,
            ITextExtractionService textExtraction,
            IEmbeddingGenerator embeddingGenerator,
            IVisionInsightService vision,
            IInsightSynthesisService insightSynthesis,
            ITemplateSuggestionService templateSuggestions,
            TimeProvider clock,
            DocMindOptions options,
            ILogger outerLogger)
        {
            _job = job;
            _document = document;
            _eventSink = eventSink;
            _textExtraction = textExtraction;
            _embeddingGenerator = embeddingGenerator;
            _vision = vision;
            _insightSynthesis = insightSynthesis;
            _templateSuggestions = templateSuggestions;
            _clock = clock;
            _options = options;
            _outerLogger = outerLogger;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var currentStage = _job.Stage;
            while (currentStage != DocumentProcessingStage.Complete)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    currentStage = currentStage switch
                    {
                        DocumentProcessingStage.ExtractText => await ExecuteExtractionAsync(cancellationToken).ConfigureAwait(false),
                        DocumentProcessingStage.ExtractVision => await ExecuteVisionAsync(cancellationToken).ConfigureAwait(false),
                        DocumentProcessingStage.GenerateInsights => await ExecuteInsightsAsync(cancellationToken).ConfigureAwait(false),
                        DocumentProcessingStage.Aggregate => await ExecuteAggregationAsync(cancellationToken).ConfigureAwait(false),
                        _ => await CompleteAsync(cancellationToken).ConfigureAwait(false)
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await HandleFailureAsync(currentStage, ex, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }

        private async Task<DocumentProcessingStage> ExecuteExtractionAsync(CancellationToken cancellationToken)
        {
            var startedAt = await MarkStageRunningAsync(DocumentProcessingStage.ExtractText, DocumentProcessingStatus.Extracting, cancellationToken).ConfigureAwait(false);

            await _eventSink.RecordAsync(
                new DocumentProcessingEventEntry(
                    Guid.Parse(_document.Id),
                    DocumentProcessingStage.ExtractText,
                    DocumentProcessingStatus.Extracting,
                    Detail: "Starting text extraction",
                    Attempt: _job.Attempt,
                    CorrelationId: _job.CorrelationId),
                cancellationToken).ConfigureAwait(false);

            var extractionResult = await _textExtraction.ExtractAsync(_document, cancellationToken).ConfigureAwait(false);

            await PurgeExistingChunksAsync(cancellationToken).ConfigureAwait(false);

            var chunkRefs = new List<ChunkReference>();
            var persistedChunks = new List<DocumentChunk>();

            for (var index = 0; index < extractionResult.Chunks.Count; index++)
            {
                var chunk = extractionResult.Chunks[index];
                var entity = new DocumentChunk
                {
                    SourceDocumentId = _job.SourceDocumentId,
                    Order = chunk.Index,
                    Text = chunk.Content.Trim(),
                    CharacterCount = chunk.Content.Length,
                    TokenCount = EstimateTokens(chunk.Content),
                    IsLastChunk = index == extractionResult.Chunks.Count - 1,
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
                    var embedding = await _embeddingGenerator.GenerateAsync(entity.Text, cancellationToken).ConfigureAwait(false);
                    if (embedding is not null && embedding.Length > 0)
                    {
                        await DocumentChunkEmbeddingWriter.UpsertAsync(Guid.Parse(entity.Id), _job.SourceDocumentId, embedding, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            _document.Summary.TextExtracted = true;
            _document.Summary.ContainsImages = extractionResult.ContainsImages;
            _document.Summary.ChunkRefs = new List<ChunkReference>(chunkRefs);
            _document.Summary.LastCompletedStage = DocumentProcessingStage.GenerateChunks;
            _document.Summary.LastKnownStatus = DocumentProcessingStatus.Extracted;
            _document.Summary.LastStageCompletedAt = _clock.GetUtcNow();
            _document.Summary.PrimaryFindings = extractionResult.Text.Length > 320
                ? extractionResult.Text[..320]
                : extractionResult.Text;
            _document.LastProcessedAt = _clock.GetUtcNow();
            _document.Status = DocumentProcessingStatus.Extracted;
            await _document.Save(cancellationToken).ConfigureAwait(false);

            var extractionMetrics = new Dictionary<string, double>
            {
                ["chunk.count"] = chunkRefs.Count,
                ["word.count"] = extractionResult.WordCount,
                ["page.count"] = extractionResult.PageCount,
                ["contains.images"] = extractionResult.ContainsImages ? 1 : 0
            };

            var extractionContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DocumentAnalysisDiagnostics.ApplyDiagnostics(extractionMetrics, extractionContext, extractionResult.Diagnostics, "extract");

            var duration = _clock.GetUtcNow() - startedAt;

            await _eventSink.RecordAsync(
                new DocumentProcessingEventEntry(
                    Guid.Parse(_document.Id),
                    DocumentProcessingStage.GenerateChunks,
                    DocumentProcessingStatus.Extracted,
                    Detail: $"Generated {chunkRefs.Count} chunks",
                    Metrics: extractionMetrics,
                    Context: extractionContext,
                    Attempt: _job.Attempt,
                    CorrelationId: _job.CorrelationId,
                    Duration: duration),
                cancellationToken).ConfigureAwait(false);

            _job.Extraction = new DocumentExtractionSnapshot
            {
                Text = extractionResult.Text,
                WordCount = extractionResult.WordCount,
                PageCount = extractionResult.PageCount,
                ContainsImages = extractionResult.ContainsImages,
                Language = extractionResult.Language
            };

            _extractionResult = extractionResult;
            _persistedChunks = persistedChunks;

            var nextStage = extractionResult.ContainsImages && _options.Processing.EnableVisionExtraction
                ? DocumentProcessingStage.ExtractVision
                : DocumentProcessingStage.GenerateInsights;

            await MarkStageCompletedAsync(
                DocumentProcessingStage.ExtractText,
                nextStage,
                DocumentProcessingStatus.Extracted,
                duration,
                inputTokens: null,
                outputTokens: null,
                cancellationToken).ConfigureAwait(false);

            return nextStage;
        }

        private async Task<DocumentProcessingStage> ExecuteVisionAsync(CancellationToken cancellationToken)
        {
            var startedAt = await MarkStageRunningAsync(DocumentProcessingStage.ExtractVision, DocumentProcessingStatus.Analyzing, cancellationToken).ConfigureAwait(false);

            long? tokensIn = null;
            long? tokensOut = null;

            var visionResult = await _vision.TryExtractAsync(_document, cancellationToken).ConfigureAwait(false);
            if (visionResult is not null && !string.IsNullOrWhiteSpace(visionResult.Narrative))
            {
                var insightRefs = new List<InsightReference>();
                var documentId = Guid.Parse(_document.Id);
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

                if (insightRefs.Count > 0)
                {
                    _document.Summary.VisionExtracted = true;
                    _document.Summary.InsightRefs.AddRange(insightRefs);
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
                    visionContext["vision.structured"] = System.Text.Json.JsonSerializer.Serialize(visionResult.StructuredPayload);
                }
                if (!string.IsNullOrWhiteSpace(visionResult.ExtractedText))
                {
                    visionContext["vision.ocrText"] = visionResult.ExtractedText.Length > 200
                        ? visionResult.ExtractedText[..200] + "…"
                        : visionResult.ExtractedText;
                }

                tokensIn = TryReadDiagnosticToken(visionResult.Diagnostics, "tokensIn");
                tokensOut = TryReadDiagnosticToken(visionResult.Diagnostics, "tokensOut");

                var duration = _clock.GetUtcNow() - startedAt;

                await _eventSink.RecordAsync(
                    new DocumentProcessingEventEntry(
                        documentId,
                        DocumentProcessingStage.ExtractVision,
                        DocumentProcessingStatus.Analyzing,
                        Detail: "Vision insights captured",
                        Metrics: visionMetrics,
                        Context: visionContext,
                        Attempt: _job.Attempt,
                        CorrelationId: _job.CorrelationId,
                        InputTokens: tokensIn,
                        OutputTokens: tokensOut,
                        Duration: duration),
                    cancellationToken).ConfigureAwait(false);
            }

            var totalDuration = _clock.GetUtcNow() - startedAt;

            await MarkStageCompletedAsync(
                DocumentProcessingStage.ExtractVision,
                DocumentProcessingStage.GenerateInsights,
                DocumentProcessingStatus.Analyzing,
                totalDuration,
                tokensIn,
                tokensOut,
                cancellationToken).ConfigureAwait(false);

            return DocumentProcessingStage.GenerateInsights;
        }

        private async Task<DocumentProcessingStage> ExecuteInsightsAsync(CancellationToken cancellationToken)
        {
            var startedAt = await MarkStageRunningAsync(DocumentProcessingStage.GenerateInsights, DocumentProcessingStatus.Analyzing, cancellationToken).ConfigureAwait(false);

            var extraction = await EnsureExtractionAsync(cancellationToken).ConfigureAwait(false);
            var chunks = await EnsureChunksAsync(cancellationToken).ConfigureAwait(false);

            var result = await _insightSynthesis.GenerateAsync(_document, extraction, chunks, cancellationToken).ConfigureAwait(false);
            var textInsights = result.Insights;

            var insightRefs = new List<InsightReference>(_document.Summary.InsightRefs);
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
                _document.Summary.PrimaryFindings = summaryFromInsights;
            }

            _document.Summary.InsightRefs = new List<InsightReference>(insightRefs);
            _document.Summary.LastCompletedStage = DocumentProcessingStage.GenerateInsights;
            _document.Summary.LastKnownStatus = DocumentProcessingStatus.InsightsReady;
            _document.Summary.LastStageCompletedAt = _clock.GetUtcNow();
            _document.Status = DocumentProcessingStatus.InsightsReady;
            _document.LastProcessedAt = _clock.GetUtcNow();
            await _document.Save(cancellationToken).ConfigureAwait(false);

            var duration = _clock.GetUtcNow() - startedAt;

            await _eventSink.RecordAsync(
                new DocumentProcessingEventEntry(
                    Guid.Parse(_document.Id),
                    DocumentProcessingStage.GenerateInsights,
                    DocumentProcessingStatus.InsightsReady,
                    Detail: $"Persisted {insightRefs.Count} insights",
                    Attempt: _job.Attempt,
                    CorrelationId: _job.CorrelationId,
                    Metrics: result.Metrics,
                    Context: result.Context,
                    InputTokens: result.InputTokens,
                    OutputTokens: result.OutputTokens,
                    Duration: duration),
                cancellationToken).ConfigureAwait(false);

            await MarkStageCompletedAsync(
                DocumentProcessingStage.GenerateInsights,
                DocumentProcessingStage.Aggregate,
                DocumentProcessingStatus.InsightsReady,
                duration,
                result.InputTokens,
                result.OutputTokens,
                cancellationToken).ConfigureAwait(false);

            return DocumentProcessingStage.Aggregate;
        }

        private async Task<DocumentProcessingStage> ExecuteAggregationAsync(CancellationToken cancellationToken)
        {
            var startedAt = await MarkStageRunningAsync(DocumentProcessingStage.Aggregate, DocumentProcessingStatus.Analyzing, cancellationToken).ConfigureAwait(false);

            var chunks = await EnsureChunksAsync(cancellationToken).ConfigureAwait(false);
            var suggestions = await _templateSuggestions.SuggestAsync(_document, chunks, cancellationToken).ConfigureAwait(false);

            var suggestionMetrics = new Dictionary<string, double>
            {
                ["suggestion.count"] = suggestions.Count,
                ["suggestion.autoAccepted"] = suggestions.Count(s => s.AutoAccepted)
            };

            var modeGroups = suggestions
                .Select(s => s.Diagnostics.TryGetValue("mode", out var mode) ? mode : "unknown")
                .GroupBy(mode => string.IsNullOrWhiteSpace(mode) ? "unknown" : mode, StringComparer.OrdinalIgnoreCase);

            foreach (var modeGroup in modeGroups)
            {
                var key = $"suggestion.mode.{modeGroup.Key.ToLowerInvariant()}";
                suggestionMetrics[key] = modeGroup.Count();
            }

            if (suggestions.Count == 0)
            {
                suggestionMetrics["suggestion.mode.none"] = 1;
            }

            if (suggestions.Count > 0)
            {
                _document.Summary.AutoClassificationConfidence = suggestions.Max(s => s.Confidence);
                var topSuggestion = suggestions[0];
                if (topSuggestion.AutoAccepted && string.IsNullOrWhiteSpace(_document.AssignedProfileId))
                {
                    _document.AssignedProfileId = topSuggestion.ProfileId;
                    _document.AssignedBySystem = true;
                }

                suggestionMetrics["suggestion.top.confidence"] = topSuggestion.Confidence;
                if (topSuggestion.Diagnostics.TryGetValue("vectorLatencyMs", out var latencyValue)
                    && double.TryParse(latencyValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var latencyMs))
                {
                    suggestionMetrics["suggestion.vectorLatencyMs"] = latencyMs;
                }
            }

            var duration = _clock.GetUtcNow() - startedAt;

            Dictionary<string, string>? suggestionContext = null;
            if (suggestions.Count > 0)
            {
                var topSuggestion = suggestions[0];
                suggestionContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["topProfileId"] = topSuggestion.ProfileId,
                    ["topConfidence"] = topSuggestion.Confidence.ToString("0.000", CultureInfo.InvariantCulture),
                    ["autoAccepted"] = topSuggestion.AutoAccepted ? "true" : "false"
                };

                if (topSuggestion.Diagnostics.TryGetValue("mode", out var modeValue) && !string.IsNullOrWhiteSpace(modeValue))
                {
                    suggestionContext["mode"] = modeValue;
                }

                if (topSuggestion.Diagnostics.TryGetValue("category", out var category) && !string.IsNullOrWhiteSpace(category))
                {
                    suggestionContext["category"] = category;
                }
            }

            await _eventSink.RecordAsync(
                new DocumentProcessingEventEntry(
                    Guid.Parse(_document.Id),
                    DocumentProcessingStage.Aggregate,
                    DocumentProcessingStatus.Analyzing,
                    Detail: "Template suggestions evaluated",
                    Attempt: _job.Attempt,
                    CorrelationId: _job.CorrelationId,
                    Metrics: suggestionMetrics,
                    Context: suggestionContext,
                    Duration: duration),
                cancellationToken).ConfigureAwait(false);

            _document.Status = DocumentProcessingStatus.Completed;
            _document.LastProcessedAt = _clock.GetUtcNow();
            _document.Summary.LastCompletedStage = DocumentProcessingStage.Complete;
            _document.Summary.LastKnownStatus = DocumentProcessingStatus.Completed;
            _document.Summary.LastStageCompletedAt = _clock.GetUtcNow();
            await _document.Save(cancellationToken).ConfigureAwait(false);

            await _eventSink.RecordAsync(
                new DocumentProcessingEventEntry(
                    Guid.Parse(_document.Id),
                    DocumentProcessingStage.Complete,
                    DocumentProcessingStatus.Completed,
                    Detail: "Document processing completed",
                    Attempt: _job.Attempt,
                    CorrelationId: _job.CorrelationId,
                    Duration: duration,
                    IsTerminal = true),
                cancellationToken).ConfigureAwait(false);

            await DocumentDiscoveryProjectionBuilder.RefreshAsync(_clock, cancellationToken).ConfigureAwait(false);

            await MarkStageCompletedAsync(
                DocumentProcessingStage.Aggregate,
                DocumentProcessingStage.Complete,
                DocumentProcessingStatus.Completed,
                duration,
                inputTokens: null,
                outputTokens: null,
                cancellationToken).ConfigureAwait(false);

            return DocumentProcessingStage.Complete;
        }

        private async Task<DocumentProcessingStage> CompleteAsync(CancellationToken cancellationToken)
        {
            await MarkStageCompletedAsync(
                DocumentProcessingStage.Complete,
                nextStage: null,
                finalStatus: DocumentProcessingStatus.Completed,
                duration: null,
                inputTokens: null,
                outputTokens: null,
                cancellationToken).ConfigureAwait(false);

            return DocumentProcessingStage.Complete;
        }

        private async Task HandleFailureAsync(DocumentProcessingStage stage, Exception exception, CancellationToken cancellationToken)
        {
            _outerLogger.LogError(exception, "Pipeline failed for document {DocumentId}", _document.Id);

            var now = _clock.GetUtcNow();
            _document.Status = DocumentProcessingStatus.Failed;
            _document.LastError = exception.Message;
            _document.LastProcessedAt = now;
            _document.Summary.LastKnownStatus = DocumentProcessingStatus.Failed;
            _document.Summary.LastStageCompletedAt = now;
            await _document.Save(cancellationToken).ConfigureAwait(false);

            var state = _job.GetStageState(stage);
            state.LastError = exception.Message;
            state.LastCompletedAt = now;
            state.LastStatus = DocumentProcessingStatus.Failed;
            state.FailureCount++;
            state.LastDuration = state.LastStartedAt.HasValue ? now - state.LastStartedAt.Value : null;
            var lastAttempt = state.Attempts.LastOrDefault();
            if (lastAttempt is not null)
            {
                lastAttempt.CompletedAt = now;
                lastAttempt.Status = DocumentProcessingStatus.Failed;
                lastAttempt.Duration = state.LastDuration;
                lastAttempt.Error = exception.Message;
            }

            var backoff = CalculateBackoff(TimeSpan.FromSeconds(Math.Max(1, _options.Processing.RetryInitialDelaySeconds)), _job.RetryCount + 1, _options.Processing);
            var nextAttemptAt = now + backoff;

            _job.RetryCount++;
            _job.Attempt = 0;
            _job.Stage = stage;
            _job.Status = _job.RetryCount >= _job.MaxAttempts
                ? DocumentProcessingStatus.Failed
                : DocumentProcessingStatus.Queued;
            _job.LastError = exception.Message;
            _job.NextAttemptAt = _job.RetryCount >= _job.MaxAttempts ? null : nextAttemptAt;
            _job.UpdatedAt = now;
            _job.CompletedAt = now;

            if (_job.RetryCount < _job.MaxAttempts)
            {
                state.LastStatus = DocumentProcessingStatus.Queued;
                state.LastQueuedAt = nextAttemptAt;
            }

            await _job.Save(cancellationToken).ConfigureAwait(false);

            var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage"] = stage.ToString(),
                ["nextAttemptAt"] = _job.NextAttemptAt?.ToString("O") ?? string.Empty
            };

            await _eventSink.RecordAsync(
                new DocumentProcessingEventEntry(
                    Guid.Parse(_document.Id),
                    DocumentProcessingStage.Failed,
                    DocumentProcessingStatus.Failed,
                    Detail: "Processing failed",
                    Error: exception.Message,
                    Context: context,
                    Attempt: _job.Attempt,
                    CorrelationId: _job.CorrelationId,
                    Duration: state.LastDuration,
                    IsTerminal: _job.RetryCount >= _job.MaxAttempts),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<DateTimeOffset> MarkStageRunningAsync(DocumentProcessingStage stage, DocumentProcessingStatus status, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();
            _job.Stage = stage;
            _job.Attempt++;
            _job.Status = status;
            _job.StartedAt = now;
            _job.UpdatedAt = now;
            if (string.IsNullOrWhiteSpace(_job.CorrelationId))
            {
                _job.CorrelationId = Guid.NewGuid().ToString("N");
            }

            var state = _job.GetStageState(stage);
            state.Stage = stage;
            state.LastStatus = status;
            state.LastStartedAt = now;
            state.LastCorrelationId = _job.CorrelationId;
            state.LastError = null;
            state.AttemptCount++;
            state.AppendAttempt(new DocumentProcessingStageAttempt
            {
                Attempt = state.AttemptCount,
                StartedAt = now,
                Status = status
            });

            await _job.Save(cancellationToken).ConfigureAwait(false);
            return now;
        }

        private async Task MarkStageCompletedAsync(
            DocumentProcessingStage completedStage,
            DocumentProcessingStage? nextStage,
            DocumentProcessingStatus finalStatus,
            TimeSpan? duration,
            long? inputTokens,
            long? outputTokens,
            CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();
            var state = _job.GetStageState(completedStage);
            state.LastStatus = finalStatus;
            state.LastCompletedAt = now;
            state.LastDuration = duration;
            state.LastError = null;
            state.LastInputTokens = inputTokens;
            state.LastOutputTokens = outputTokens;
            state.LastCorrelationId = _job.CorrelationId;
            state.SuccessCount++;
            var attempt = state.Attempts.LastOrDefault();
            if (attempt is not null)
            {
                attempt.CompletedAt = now;
                attempt.Status = finalStatus;
                attempt.Duration = duration;
                attempt.Error = null;
                attempt.InputTokens = inputTokens;
                attempt.OutputTokens = outputTokens;
            }

            _job.Attempt = 0;
            _job.RetryCount = 0;
            _job.Status = nextStage is null ? DocumentProcessingStatus.Completed : DocumentProcessingStatus.Queued;
            _job.CompletedAt = now;
            _job.UpdatedAt = now;
            _job.LastError = null;
            _job.NextAttemptAt = nextStage is null ? null : now;
            _job.Stage = nextStage ?? DocumentProcessingStage.Complete;

            if (nextStage.HasValue)
            {
                _job.MarkStageQueued(nextStage.Value, now, _job.CorrelationId);
            }

            await _job.Save(cancellationToken).ConfigureAwait(false);
        }

        private async Task<DocumentExtractionResult> EnsureExtractionAsync(CancellationToken cancellationToken)
        {
            if (_extractionResult is not null)
            {
                return _extractionResult;
            }

            if (_job.Extraction is null)
            {
                _outerLogger.LogWarning("Missing extraction snapshot for document {DocumentId}; rerunning extraction", _document.Id);
                var result = await _textExtraction.ExtractAsync(_document, cancellationToken).ConfigureAwait(false);
                _extractionResult = result;
                return result;
            }

            var snapshot = _job.Extraction;
            _extractionResult = new DocumentExtractionResult(
                snapshot.Text,
                Array.Empty<ExtractedChunk>(),
                snapshot.WordCount,
                snapshot.PageCount,
                snapshot.ContainsImages,
                new Dictionary<string, object?>(),
                snapshot.Language);
            return _extractionResult;
        }

        private async Task<IReadOnlyList<DocumentChunk>> EnsureChunksAsync(CancellationToken cancellationToken)
        {
            if (_persistedChunks.Count > 0)
            {
                return _persistedChunks;
            }

            var query = await DocumentChunk
                .Query($"SourceDocumentId == '{_job.SourceDocumentId}'", cancellationToken)
                .ConfigureAwait(false);
            _persistedChunks = query.OrderBy(chunk => chunk.Order).ToList();
            return _persistedChunks;
        }

        private async Task PurgeExistingChunksAsync(CancellationToken cancellationToken)
        {
            var existingChunks = await DocumentChunk
                .Query($"SourceDocumentId == '{_job.SourceDocumentId}'", cancellationToken)
                .ConfigureAwait(false);

            foreach (var chunk in existingChunks)
            {
                await chunk.Delete(cancellationToken).ConfigureAwait(false);
            }

            var existingEmbeddings = await DocumentChunkEmbedding
                .Query($"SourceDocumentId == '{_job.SourceDocumentId}'", cancellationToken)
                .ConfigureAwait(false);

            foreach (var embedding in existingEmbeddings)
            {
                await embedding.Delete(cancellationToken).ConfigureAwait(false);
            }
        }

        private static TimeSpan CalculateBackoff(TimeSpan initialDelay, int attempt, DocMindOptions.ProcessingOptions options)
        {
            var baseDelay = initialDelay.TotalMilliseconds * Math.Pow(options.RetryBackoffMultiplier <= 1 ? 1d : options.RetryBackoffMultiplier, Math.Max(0, attempt - 1));
            var maxDelay = options.RetryMaxDelaySeconds * 1000d;
            var delay = Math.Min(baseDelay, maxDelay);
            if (options.RetryUseJitter)
            {
                var random = new Random();
                var jitter = random.NextDouble() * 0.3 + 0.85;
                delay *= jitter;
            }

            return TimeSpan.FromMilliseconds(Math.Max(delay, options.RetryInitialDelaySeconds * 1000d));
        }

        private static int EstimateTokens(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return 0;
            }

            return Math.Max(1, content.Length / 4);
        }

        private static long? TryReadDiagnosticToken(IDictionary<string, object?> diagnostics, string key)
        {
            if (diagnostics.TryGetValue(key, out var value) && value is not null)
            {
                if (value is long longValue)
                {
                    return longValue;
                }

                if (value is double doubleValue)
                {
                    return (long)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
                }

                if (value is string stringValue && long.TryParse(stringValue, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
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
