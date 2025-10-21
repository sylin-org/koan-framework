using System.Collections.Generic;
using Koan.Data.Core;
using System.Linq;
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
    private readonly IDocumentClassifier _classifier;
    private readonly IRunLogWriter _runLog;
    private readonly MeridianOptions _options;
    private readonly ILogger<PipelineProcessor> _logger;

    public PipelineProcessor(
        ITextExtractor textExtractor,
        IPassageChunker chunker,
        IPassageIndexer indexer,
        IFieldExtractor fieldExtractor,
        IDocumentMerger merger,
        IDocumentClassifier classifier,
        IRunLogWriter runLog,
        MeridianOptions options,
        ILogger<PipelineProcessor> logger)
    {
        _textExtractor = textExtractor;
        _chunker = chunker;
        _indexer = indexer;
        _fieldExtractor = fieldExtractor;
        _merger = merger;
        _classifier = classifier;
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

            var previousHash = document.TextHash;
            var previousStatus = document.Status;

            var extractStarted = DateTime.UtcNow;
            var extraction = await _textExtractor.ExtractAsync(document, ct);
            var extractFinished = DateTime.UtcNow;

            var newHash = TextExtractor.ComputeTextHash(extraction.Text);

            document.Status = DocumentProcessingStatus.Extracted;
            document.ExtractionConfidence = extraction.Confidence;
            document.ExtractedAt = extractFinished;
            document.TextHash = newHash;
            document.PageCount = extraction.PageCount;
            document.ExtractedText = extraction.Text;
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

            if (previousStatus == DocumentProcessingStatus.Indexed && string.Equals(previousHash, newHash, StringComparison.Ordinal))
            {
                document.Status = DocumentProcessingStatus.Indexed;
                document.UpdatedAt = extractFinished;
                await document.Save(ct);

                await _runLog.AppendAsync(new RunLog
                {
                    PipelineId = pipeline.Id,
                    Stage = "refresh",
                    FieldPath = null,
                    StartedAt = extractFinished,
                    FinishedAt = extractFinished,
                    Status = "skipped",
                    Metadata = new Dictionary<string, string>
                    {
                        ["documentId"] = document.Id,
                        ["reason"] = "text-hash-unchanged"
                    }
                }, ct);

                continue;
            }

            var classifyStarted = DateTime.UtcNow;
            var classification = await _classifier.ClassifyAsync(document, ct);
            var classifyFinished = DateTime.UtcNow;

            document.SourceType = classification.TypeId;
            document.ClassifiedTypeId = classification.TypeId;
            document.ClassifiedTypeVersion = classification.Version;
            document.ClassificationConfidence = classification.Confidence;
            document.ClassificationMethod = classification.Method;
            document.ClassificationReason = classification.Reason;

            var allowed = pipeline.RequiredSourceTypes is null ||
                          pipeline.RequiredSourceTypes.Count == 0 ||
                          pipeline.RequiredSourceTypes.Any(type =>
                              string.Equals(type, classification.TypeId, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                var requiredList = string.Join(", ", pipeline.RequiredSourceTypes);
                var exclusionReason = $"Classified as {classification.TypeId}, but analysis '{pipeline.AnalysisTypeId}' requires [{requiredList}].";

                _logger.LogWarning("Document {DocumentId} classified as {TypeId} but pipeline {PipelineId} requires [{Required}]; excluding from run.",
                    document.Id, classification.TypeId, pipeline.Id, requiredList);

                document.Status = DocumentProcessingStatus.Failed;
                document.ClassificationReason = exclusionReason;
                document.UpdatedAt = classifyFinished;
                await document.Save(ct);

                var metadata = new Dictionary<string, string>
                {
                    ["documentId"] = document.Id,
                    ["typeId"] = classification.TypeId,
                    ["confidence"] = classification.Confidence.ToString("0.00"),
                    ["method"] = classification.Method.ToString(),
                    ["allowed"] = "false",
                    ["requiredSourceTypes"] = requiredList
                };

                await _runLog.AppendAsync(new RunLog
                {
                    PipelineId = pipeline.Id,
                    Stage = "classify",
                    FieldPath = null,
                    StartedAt = classifyStarted,
                    FinishedAt = classifyFinished,
                    Status = "excluded",
                    Metadata = metadata
                }, ct);

                continue;
            }

            document.Status = DocumentProcessingStatus.Classified;
            document.UpdatedAt = classifyFinished;
            await document.Save(ct);

            var allowedMetadata = new Dictionary<string, string>
            {
                ["documentId"] = document.Id,
                ["typeId"] = classification.TypeId,
                ["confidence"] = classification.Confidence.ToString("0.00"),
                ["method"] = classification.Method.ToString(),
                ["reason"] = classification.Reason,
                ["allowed"] = "true"
            };

            await _runLog.AppendAsync(new RunLog
            {
                PipelineId = pipeline.Id,
                Stage = "classify",
                FieldPath = null,
                StartedAt = classifyStarted,
                FinishedAt = classifyFinished,
                Status = "success",
                Metadata = allowedMetadata
            }, ct);

            var chunks = _chunker.Chunk(document, extraction.Text);
            foreach (var chunk in chunks)
            {
                var saved = await chunk.Save(ct);
                passages.Add(saved);
            }
        }

        await _indexer.IndexAsync(passages, ct);

        foreach (var docId in job.DocumentIds)
        {
            var doc = await SourceDocument.Get(docId, ct);
            if (doc is null)
            {
                continue;
            }

            if (doc.Status != DocumentProcessingStatus.Failed)
            {
                doc.Status = DocumentProcessingStatus.Indexed;
                doc.UpdatedAt = DateTime.UtcNow;
                await doc.Save(ct);
            }
        }

        var allPassages = await Passage.Query(p => p.PipelineId == pipeline.Id, ct);
        var extractions = await _fieldExtractor.ExtractAsync(pipeline, allPassages, _options, ct);

        var existingFields = await ExtractedField.Query(e => e.PipelineId == pipeline.Id, ct);
        var existingByField = existingFields.ToDictionary(e => e.FieldPath, StringComparer.Ordinal);

        var savedExtractions = new List<ExtractedField>();
        foreach (var extraction in extractions)
        {
            if (existingByField.TryGetValue(extraction.FieldPath, out var prior))
            {
                extraction.Id = prior.Id;
            }

            extraction.UpdatedAt = DateTime.UtcNow;
            var saved = await extraction.Save(ct);
            existingByField[extraction.FieldPath] = saved;
            savedExtractions.Add(saved);
        }

        await _merger.MergeAsync(pipeline, savedExtractions, ct);
    }
}
