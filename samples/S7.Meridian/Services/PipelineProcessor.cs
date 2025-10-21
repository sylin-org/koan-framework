using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IPipelineProcessor
{
    Task ProcessAsync(ProcessingJob job, CancellationToken ct);
}

public sealed class PipelineProcessor : IPipelineProcessor
{
    private readonly ITextExtractor _textExtractor;
    private readonly IPassageChunker _chunker;
    private readonly IPassageIndexer _indexer;
    private readonly IFieldExtractor _fieldExtractor;
    private readonly IDocumentMerger _merger;
    private readonly IRunLogWriter _runLog;
    private readonly MeridianOptions _options;
    private readonly ILogger<PipelineProcessor> _logger;

    public PipelineProcessor(
        ITextExtractor textExtractor,
        IPassageChunker chunker,
        IPassageIndexer indexer,
        IFieldExtractor fieldExtractor,
        IDocumentMerger merger,
        IRunLogWriter runLog,
        MeridianOptions options,
        ILogger<PipelineProcessor> logger)
    {
        _textExtractor = textExtractor;
        _chunker = chunker;
        _indexer = indexer;
        _fieldExtractor = fieldExtractor;
        _merger = merger;
        _runLog = runLog;
        _options = options;
        _logger = logger;
    }

    public async Task ProcessAsync(ProcessingJob job, CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(job.PipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline {job.PipelineId} not found.");

        var passages = new List<Passage>();
        foreach (var documentId in job.DocumentIds)
        {
            ct.ThrowIfCancellationRequested();

            var document = await SourceDocument.Get(documentId, ct);
            if (document is null)
            {
                _logger.LogWarning("Document {DocumentId} missing for pipeline {PipelineId}.", documentId, job.PipelineId);
                continue;
            }

            var extractStarted = DateTime.UtcNow;
            var extraction = await _textExtractor.ExtractAsync(document, ct);
            var extractFinished = DateTime.UtcNow;

            document.Status = DocumentProcessingStatus.Extracted;
            document.ExtractionConfidence = extraction.Confidence;
            document.ExtractedAt = extractFinished;
            document.TextHash = TextExtractor.ComputeTextHash(extraction.Text);
            document.PageCount = extraction.PageCount;
            document.UpdatedAt = extractFinished;
            await document.Save(ct);

            await _runLog.AppendAsync(new RunLog
            {
                PipelineId = pipeline.Id,
                Stage = "extract",
                FieldPath = null,
                StartedAt = extractStarted,
                FinishedAt = extractFinished,
                Status = "success",
                Metadata = new Dictionary<string, string>
                {
                    ["documentId"] = document.Id,
                    ["method"] = extraction.Method,
                    ["confidence"] = extraction.Confidence.ToString("0.00")
                }
            }, ct);

            var chunks = _chunker.Chunk(document, extraction.Text);
            foreach (var chunk in chunks)
            {
                var saved = await chunk.Save(ct);
                passages.Add(saved);
            }
        }

        await _indexer.IndexAsync(passages, ct);

        var allPassages = await Passage.Query(p => p.PipelineId == pipeline.Id, ct);
        var extractions = await _fieldExtractor.ExtractAsync(pipeline, allPassages, _options, ct);

        var savedExtractions = new List<ExtractedField>();
        foreach (var extraction in extractions)
        {
            var existing = await ExtractedField.Query(e => e.PipelineId == pipeline.Id && e.FieldPath == extraction.FieldPath, ct);
            var prior = existing.FirstOrDefault();
            if (prior is not null)
            {
                extraction.Id = prior.Id;
            }

            extraction.UpdatedAt = DateTime.UtcNow;
            var saved = await extraction.Save(ct);
            savedExtractions.Add(saved);
        }

        await _merger.MergeAsync(pipeline, savedExtractions, ct);
    }
}
