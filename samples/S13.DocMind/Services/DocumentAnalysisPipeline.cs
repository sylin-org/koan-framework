using System.Linq;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed class DocumentAnalysisOptions
{
    public int BatchSize { get; set; } = 4;
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";
    public bool EnableVisionInsights { get; set; } = true;
    public bool RecordDiagnostics { get; set; } = true;
}

public sealed class TransientDocumentProcessingException : Exception
{
    public TransientDocumentProcessingException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class DocumentAnalysisPipeline
{
    private readonly IAi _ai;
    private readonly ILogger<DocumentAnalysisPipeline> _logger;
    private readonly IOptionsMonitor<DocumentAnalysisOptions> _options;
    private readonly IDocumentPipelineQueue _queue;
    private readonly IVisionInsightService _visionInsights;
    private readonly ITemplateGeneratorService _templateGenerator;
    private readonly IInsightAggregationService _aggregationService;
    private readonly IDocumentProcessingEventSink _eventSink;

    public DocumentAnalysisPipeline(
        IAi ai,
        ILogger<DocumentAnalysisPipeline> logger,
        IOptionsMonitor<DocumentAnalysisOptions> options,
        IDocumentPipelineQueue queue,
        IVisionInsightService visionInsights,
        ITemplateGeneratorService templateGenerator,
        IInsightAggregationService aggregationService,
        IDocumentProcessingEventSink eventSink)
    {
        _ai = ai;
        _logger = logger;
        _options = options;
        _queue = queue;
        _visionInsights = visionInsights;
        _templateGenerator = templateGenerator;
        _aggregationService = aggregationService;
        _eventSink = eventSink;
    }

    public async Task ProcessBatchAsync(IReadOnlyList<DocumentWorkItem> workItems, CancellationToken ct)
    {
        if (workItems.Count == 0)
        {
            return;
        }

        var pipelineOptions = _options.CurrentValue;
        var prepared = new List<(DocumentWorkItem WorkItem, File File, DocumentType? Type, TemplateDefinition Template, VisionInsightResult? Vision, AggregatedInsights Aggregated)>();

        foreach (var workItem in workItems)
        {
            ct.ThrowIfCancellationRequested();
            _eventSink.Record(new DocumentProcessingEvent(workItem.FileId, "dequeue", "started", workItem.TraceId, workItem.CorrelationId, workItem.Attempt));

            try
            {
                var file = await File.Get(workItem.FileId);
                if (file is null)
                {
                    _eventSink.Record(new DocumentProcessingEvent(workItem.FileId, "hydrate", "missing", workItem.TraceId, workItem.CorrelationId, workItem.Attempt));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(file.DocumentTypeId))
                {
                    _eventSink.Record(new DocumentProcessingEvent(workItem.FileId, "hydrate", "no-type", workItem.TraceId, workItem.CorrelationId, workItem.Attempt));
                    continue;
                }

                var docType = await DocumentType.Get(file.DocumentTypeId);
                var template = await _templateGenerator.ResolveTemplateAsync(docType, ct);
                VisionInsightResult? vision = null;
                if (pipelineOptions.EnableVisionInsights)
                {
                    vision = await _visionInsights.TryExtractAsync(file, ct);
                }

                file.Status = "analyzing";
                file.Metadata["analysisProfile"] = workItem.Profile.Name;
                await file.Save(ct);

                var aggregated = await _aggregationService.AggregateAsync(file, template, vision, ct);

                prepared.Add((workItem, file, docType, template, vision, aggregated));
                _eventSink.Record(new DocumentProcessingEvent(workItem.FileId, "aggregate", "prepared", workItem.TraceId, workItem.CorrelationId, workItem.Attempt));
            }
            catch (TransientDocumentProcessingException tex)
            {
                await HandleTransientFailureAsync(workItem, tex, ct);
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(workItem, ex, ct);
            }
        }

        if (prepared.Count == 0)
        {
            return;
        }

        var embeddingTargets = prepared
            .Select((entry, idx) => (Entry: entry, Index: idx))
            .Where(tuple => !string.IsNullOrWhiteSpace(tuple.Entry.Aggregated.EmbeddingText))
            .ToList();

        var embeddingInputs = embeddingTargets
            .Select(tuple => tuple.Entry.Aggregated.EmbeddingText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var embeddingMap = new Dictionary<int, float[]>();
        if (embeddingInputs.Count > 0 && !string.IsNullOrWhiteSpace(pipelineOptions.EmbeddingModel))
        {
            try
            {
                var response = await _ai.EmbedAsync(new AiEmbeddingsRequest
                {
                    Input = embeddingInputs,
                    Model = pipelineOptions.EmbeddingModel
                }, ct);
                for (var i = 0; i < embeddingTargets.Count && i < response.Vectors.Count; i++)
                {
                    embeddingMap[embeddingTargets[i].Index] = response.Vectors[i];
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding batch failed for {Count} documents", embeddingInputs.Count);
            }
        }

        for (var index = 0; index < prepared.Count; index++)
        {
            var entry = prepared[index];
            var workItem = entry.WorkItem;
            try
            {
                embeddingMap.TryGetValue(index, out var embedding);
                await PersistAnalysisAsync(entry, embedding, ct);
                workItem.MarkAttemptCompleted(success: true);
                _eventSink.Record(new DocumentProcessingEvent(workItem.FileId, "persist", "completed", workItem.TraceId, workItem.CorrelationId, workItem.Attempt));
            }
            catch (TransientDocumentProcessingException tex)
            {
                workItem.MarkAttemptCompleted(success: false);
                await HandleTransientFailureAsync(workItem, tex, ct);
            }
            catch (Exception ex)
            {
                workItem.MarkAttemptCompleted(success: false);
                await HandleFailureAsync(workItem, ex, ct);
            }
        }
    }

    private async Task PersistAnalysisAsync(
        (DocumentWorkItem WorkItem, File File, DocumentType? Type, TemplateDefinition Template, VisionInsightResult? Vision, AggregatedInsights Aggregated) context,
        float[]? embedding,
        CancellationToken ct)
    {
        var file = context.File;
        var workItem = context.WorkItem;
        var aggregated = context.Aggregated;

        var analysis = new Analysis
        {
            FileId = file.Id!,
            FileName = file.Name,
            DocumentTypeId = context.Type?.Id ?? file.DocumentTypeId ?? string.Empty,
            DocumentTypeName = context.Type?.Name ?? "Unassigned",
            Status = "completed",
            StartedDate = workItem.LastDequeuedAt?.UtcDateTime ?? DateTime.UtcNow,
            CompletedDate = DateTime.UtcNow,
            OverallConfidence = aggregated.FieldConfidences.Values.DefaultIfEmpty(0.5).Average(),
            ExtractedData = aggregated.StructuredData,
            FieldConfidences = aggregated.FieldConfidences,
            RawAIResponse = aggregated.Summary,
            ProcessingMetadata = new Dictionary<string, object>
            {
                ["traceId"] = workItem.TraceId,
                ["correlationId"] = workItem.CorrelationId,
                ["profile"] = workItem.Profile.Name,
                ["attempt"] = workItem.Attempt
            }
        };

        if (embedding is not null)
        {
            analysis.ProcessingMetadata["embedding"] = embedding;
            analysis.ProcessingMetadata["embeddingModel"] = _options.CurrentValue.EmbeddingModel;
        }

        if (_options.CurrentValue.RecordDiagnostics)
        {
            foreach (var diagnostic in aggregated.Diagnostics)
            {
                analysis.ProcessingMetadata[$"diag.{diagnostic.Key}"] = diagnostic.Value;
            }
        }

        await analysis.Save(ct);

        file.AnalysisId = analysis.Id;
        file.Status = "completed";
        file.CompletedDate = DateTime.UtcNow;
        file.Metadata["analysisTraceId"] = workItem.TraceId;
        file.Metadata["analysisProfile"] = workItem.Profile.Name;
        await file.Save(ct);
    }

    private async Task HandleTransientFailureAsync(DocumentWorkItem workItem, Exception exception, CancellationToken ct)
    {
        _eventSink.Record(new DocumentProcessingEvent(workItem.FileId, "pipeline", "transient", workItem.TraceId, workItem.CorrelationId, workItem.Attempt, Exception: exception));
        var requeued = await _queue.ScheduleRetryAsync(workItem, exception, ct);
        if (!requeued)
        {
            await HandleFailureAsync(workItem, exception, ct, isTerminal: true);
        }
    }

    private async Task HandleFailureAsync(DocumentWorkItem workItem, Exception exception, CancellationToken ct, bool isTerminal = false)
    {
        _logger.LogError(exception, "Document {DocumentId} processing failed", workItem.FileId);
        _eventSink.Record(new DocumentProcessingEvent(workItem.FileId, "pipeline", isTerminal ? "failed" : "error", workItem.TraceId, workItem.CorrelationId, workItem.Attempt, Exception: exception));

        var file = await File.Get(workItem.FileId);
        if (file is null)
        {
            return;
        }

        file.Status = "failed";
        file.ErrorMessage = exception.Message;
        file.LastErrorDate = DateTime.UtcNow;
        await file.Save(ct);
    }
}
