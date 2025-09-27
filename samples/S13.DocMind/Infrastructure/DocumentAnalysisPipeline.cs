using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
                        IsLastChunk = chunkIndex == extractionResult.Chunks.Count - 1
                    };

                    entity = await entity.Save(cancellationToken).ConfigureAwait(false);
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
                document.Summary.ChunkRefs = chunkRefs;
                document.LastProcessedAt = clock.GetUtcNow();
                document.Status = DocumentProcessingStatus.Extracted;
                document.Summary.PrimaryFindings = extractionResult.Text.Length > 320
                    ? extractionResult.Text[..320]
                    : extractionResult.Text;
                await document.Save(cancellationToken).ConfigureAwait(false);

                await eventSink.RecordAsync(
                    new DocumentProcessingEventEntry(
                        documentId,
                        DocumentProcessingStage.GenerateChunks,
                        DocumentProcessingStatus.Extracted,
                        Detail: $"Generated {chunkRefs.Count} chunks",
                        Metrics: new Dictionary<string, double>
                        {
                            ["chunkCount"] = chunkRefs.Count,
                            ["wordCount"] = extractionResult.WordCount,
                            ["pageCount"] = extractionResult.PageCount
                        },
                        Attempt: workItem.Attempt,
                        CorrelationId: workItem.CorrelationId),
                    cancellationToken).ConfigureAwait(false);

                document.Status = DocumentProcessingStatus.Completed;
                document.LastProcessedAt = clock.GetUtcNow();
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
