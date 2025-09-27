using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Vector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.Infrastructure;

public sealed class DocumentAnalysisPipeline : BackgroundService
{
    private readonly IDocumentPipelineQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DocMindOptions _options;
    private readonly ILogger<DocumentAnalysisPipeline> _logger;

    public DocumentAnalysisPipeline(IDocumentPipelineQueue queue, IServiceScopeFactory scopeFactory, IOptions<DocMindOptions> options, ILogger<DocumentAnalysisPipeline> logger)
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
            var task = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await ProcessDocumentAsync(scope.ServiceProvider, work, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pipeline failed for document {DocumentId}", work.DocumentId);
                }
                finally
                {
                    semaphore.Release();
                }
            }, stoppingToken);
            running.Add(task);
        }

        await Task.WhenAll(running); // Drain outstanding work before shutdown
    }

    private static async Task ProcessDocumentAsync(IServiceProvider services, DocumentWorkItem workItem, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILogger<DocumentAnalysisPipeline>>();
        var extraction = services.GetRequiredService<ITextExtractionService>();
        var insights = services.GetRequiredService<IInsightSynthesisService>();
        var templates = services.GetRequiredService<ITemplateSuggestionService>();
        var embeddingGenerator = services.GetRequiredService<IEmbeddingGenerator>();
        var clock = services.GetRequiredService<TimeProvider>();

        var document = await SourceDocument.Get(workItem.DocumentId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            logger.LogWarning("Document {DocumentId} missing from storage", workItem.DocumentId);
            return;
        }

        try
        {
            await RecordEventAsync(document, DocumentProcessingStage.Extraction, DocumentProcessingStatus.Extracting, "Extraction started", cancellationToken).ConfigureAwait(false);
            document.MarkStatus(DocumentProcessingStatus.Extracting);
            await document.Save(cancellationToken).ConfigureAwait(false);

            var extractionResult = await extraction.ExtractAsync(document, cancellationToken).ConfigureAwait(false);
            document.Summary.TextExtracted = true;
            document.Summary.WordCount = extractionResult.WordCount;
            document.Summary.PageCount = extractionResult.PageCount;
            document.Summary.ChunkCount = extractionResult.Chunks.Count;
            document.Summary.Excerpt = extractionResult.Text.Length > 320 ? extractionResult.Text[..320] : extractionResult.Text;
            document.MarkStatus(DocumentProcessingStatus.Extracted);
            await document.Save(cancellationToken).ConfigureAwait(false);

            await RecordEventAsync(document, DocumentProcessingStage.Chunking, DocumentProcessingStatus.Extracted, "Chunking document", cancellationToken).ConfigureAwait(false);

            var savedChunks = new List<DocumentChunk>();
            foreach (var chunk in extractionResult.Chunks)
            {
                var chunkEntity = new DocumentChunk
                {
                    DocumentId = document.Id,
                    Index = chunk.Index,
                    Channel = chunk.Channel,
                    Content = chunk.Content,
                    Summary = chunk.Summary,
                    TokenEstimate = EstimateTokens(chunk.Content)
                };

                var embedding = await embeddingGenerator.GenerateAsync(chunk.Content, cancellationToken).ConfigureAwait(false);
                chunkEntity.Embedding = embedding;
                chunkEntity = await chunkEntity.Save(cancellationToken).ConfigureAwait(false);
                savedChunks.Add(chunkEntity);

                if (embedding is not null && Vector<DocumentChunk>.IsAvailable)
                {
                    await Vector<DocumentChunk>.Save(chunkEntity.Id, embedding, cancellationToken).ConfigureAwait(false);
                }
            }

            document.ChunkIds = savedChunks.Select(c => c.Id).ToList();
            document.MarkStatus(DocumentProcessingStatus.Analyzing);
            await document.Save(cancellationToken).ConfigureAwait(false);

            await RecordEventAsync(document, DocumentProcessingStage.Insight, DocumentProcessingStatus.Analyzing, "Synthesising insights", cancellationToken).ConfigureAwait(false);
            var insightEntities = await insights.GenerateAsync(document, extractionResult, savedChunks, cancellationToken).ConfigureAwait(false);

            foreach (var insight in insightEntities)
            {
                await insight.Save(cancellationToken).ConfigureAwait(false);
            }

            if (insightEntities.Count > 0)
            {
                document.Summary.PrimaryFindings = insightEntities[0].Content.Length > 240
                    ? insightEntities[0].Content[..240]
                    : insightEntities[0].Content;
            }

            await RecordEventAsync(document, DocumentProcessingStage.Suggestion, DocumentProcessingStatus.Analyzing, "Computing profile suggestions", cancellationToken).ConfigureAwait(false);
            var suggestions = await templates.SuggestAsync(document, savedChunks, cancellationToken).ConfigureAwait(false);
            document.Suggestions = suggestions.ToList();
            if (string.IsNullOrWhiteSpace(document.AssignedProfileId) && suggestions.Count > 0)
            {
                var top = suggestions[0];
                document.AssignedProfileId = top.ProfileId;
                document.AssignedBySystem = true;
                document.Suggestions[0].AutoAccepted = true;
                await RecordEventAsync(document, DocumentProcessingStage.Suggestion, DocumentProcessingStatus.Analyzing,
                    $"Auto-assigned profile {top.ProfileId}", cancellationToken).ConfigureAwait(false);
            }

            document.MarkStatus(DocumentProcessingStatus.Completed);
            document.CompletedAt = clock.GetUtcNow();
            await document.Save(cancellationToken).ConfigureAwait(false);
            await RecordEventAsync(document, DocumentProcessingStage.Completion, DocumentProcessingStatus.Completed, "Document analysis completed", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Processing failed for document {DocumentId}", document.Id);
            document.MarkStatus(DocumentProcessingStatus.Failed, ex.Message);
            await document.Save(cancellationToken).ConfigureAwait(false);
            await RecordEventAsync(document, DocumentProcessingStage.Error, DocumentProcessingStatus.Failed, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task RecordEventAsync(SourceDocument document, DocumentProcessingStage stage, DocumentProcessingStatus status, string message, CancellationToken cancellationToken)
    {
        var evt = new DocumentProcessingEvent
        {
            DocumentId = document.Id,
            Stage = stage,
            Status = status,
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow
        };
        return evt.Save(cancellationToken);
    }

    private static int EstimateTokens(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;
        return Math.Max(1, content.Length / 4);
    }
}
