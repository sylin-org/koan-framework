using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly INotesExtractionService _notesExtractor;
    private readonly IDocumentMerger _merger;
    private readonly IDocumentClassifier _classifier;
    private readonly IRunLogWriter _runLog;
    private readonly IIncrementalRefreshPlanner _refreshPlanner;
    private readonly MeridianOptions _options;
    private readonly ILogger<PipelineProcessor> _logger;

    public PipelineProcessor(
        ITextExtractor textExtractor,
        IPassageChunker chunker,
        IPassageIndexer indexer,
        IFieldExtractor fieldExtractor,
        INotesExtractionService notesExtractor,
        IDocumentMerger merger,
        IDocumentClassifier classifier,
        IRunLogWriter runLog,
        IIncrementalRefreshPlanner refreshPlanner,
        MeridianOptions options,
        ILogger<PipelineProcessor> logger)
    {
        _textExtractor = textExtractor;
        _chunker = chunker;
        _indexer = indexer;
        _fieldExtractor = fieldExtractor;
        _notesExtractor = notesExtractor;
        _merger = merger;
        _classifier = classifier;
        _runLog = runLog;
        _refreshPlanner = refreshPlanner;
        _options = options;
        _logger = logger;
    }

    public async Task ProcessAsync(ProcessingJob job, CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(job.PipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline {job.PipelineId} not found.");

        job.TotalDocuments = job.DocumentIds.Count;
        job.ProcessedDocuments = 0;
        // Status already set to Processing by TryClaimAnyAsync, no need to save again

        pipeline.TotalDocuments = job.TotalDocuments;
        pipeline.ProcessedDocuments = 0;
        pipeline.Status = PipelineStatus.Processing;
        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct).ConfigureAwait(false);

        // STEP 1: Create virtual document from Authoritative Notes (if present)
        string? virtualDocumentId = null;
        if (!string.IsNullOrWhiteSpace(pipeline.AuthoritativeNotes))
        {
            virtualDocumentId = await CreateVirtualDocumentFromNotesAsync(pipeline, ct);
            _logger.LogInformation(
                "Created virtual document {VirtualDocId} from Authoritative Notes for pipeline {PipelineId}",
                virtualDocumentId,
                pipeline.Id);
        }

        var changedDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var passages = new List<Passage>();
        var processedCount = 0;

        foreach (var documentId in job.DocumentIds)
        {
            ct.ThrowIfCancellationRequested();

            var document = await SourceDocument.Get(documentId, ct);
            if (document is null)
            {
                _logger.LogWarning("Document {DocumentId} missing for pipeline {PipelineId}.", documentId, job.PipelineId);
                processedCount++;
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
                DocumentId = document.Id,
                FieldPath = null,
                StartedAt = extractStarted,
                FinishedAt = extractFinished,
                Status = "success",
                Metadata = new Dictionary<string, string>
                {
                    ["method"] = extraction.Method,
                    ["confidence"] = extraction.Confidence.ToString("0.00"),
                    ["pageCount"] = extraction.PageCount.ToString(CultureInfo.InvariantCulture)
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
                    DocumentId = document.Id,
                    FieldPath = null,
                    StartedAt = extractFinished,
                    FinishedAt = extractFinished,
                    Status = "skipped",
                    Metadata = new Dictionary<string, string>
                    {
                        ["reason"] = "text-hash-unchanged"
                    }
                }, ct);

                processedCount++;
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
                var requiredList = pipeline.RequiredSourceTypes is { Count: > 0 }
                    ? string.Join(", ", pipeline.RequiredSourceTypes)
                    : "(unspecified)";
                var exclusionReason = $"Classified as {classification.TypeId}, but analysis '{pipeline.AnalysisTypeId}' requires [{requiredList}].";

                _logger.LogWarning("Document {DocumentId} classified as {TypeId} but pipeline {PipelineId} requires [{Required}]; excluding from run.",
                    document.Id, classification.TypeId, pipeline.Id, requiredList);

                document.Status = DocumentProcessingStatus.Failed;
                document.ClassificationReason = exclusionReason;
                document.UpdatedAt = classifyFinished;
                await document.Save(ct);

                var metadata = new Dictionary<string, string>
                {
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
                    DocumentId = document.Id,
                    FieldPath = null,
                    StartedAt = classifyStarted,
                    FinishedAt = classifyFinished,
                    Status = "excluded",
                    ModelId = classification.Method.ToString(),
                    ErrorMessage = exclusionReason,
                    Metadata = metadata
                }, ct);

                processedCount++;
                continue;
            }

            document.Status = DocumentProcessingStatus.Classified;
            document.UpdatedAt = classifyFinished;
            await document.Save(ct);

            changedDocuments.Add(document.Id);

            var allowedMetadata = new Dictionary<string, string>
            {
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
                DocumentId = document.Id,
                FieldPath = null,
                StartedAt = classifyStarted,
                FinishedAt = classifyFinished,
                Status = "success",
                ModelId = classification.Method.ToString(),
                Metadata = allowedMetadata
            }, ct);

            var chunks = _chunker.Chunk(document, extraction.Text);
            foreach (var chunk in chunks)
            {
                var saved = await chunk.Save(ct);
                passages.Add(saved);
            }

            processedCount++;
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

        pipeline.ProcessedDocuments = job.ProcessedDocuments;
        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct);

        var allPassages = await Passage.Query(p => p.PipelineId == pipeline.Id, ct);
        var existingFields = (await ExtractedField.Query(e => e.PipelineId == pipeline.Id, ct).ConfigureAwait(false))
            .Select(field =>
            {
                field.FieldPath = FieldPathCanonicalizer.Canonicalize(field.FieldPath);
                return field;
            })
            .ToList();
        var plan = await _refreshPlanner.PlanAsync(pipeline, changedDocuments, existingFields, ct).ConfigureAwait(false);

        var planTimestamp = DateTime.UtcNow;
        var planMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = plan.Mode,
            ["changedDocuments"] = changedDocuments.Count.ToString(CultureInfo.InvariantCulture),
            ["fieldsToExtract"] = plan.RequiresFullExtraction ? "all" : plan.FieldsToExtract.Count.ToString(CultureInfo.InvariantCulture),
            ["fieldsPreserved"] = plan.RequiresFullExtraction ? "0" : plan.FieldsToPreserve.Count.ToString(CultureInfo.InvariantCulture)
        };

        if (!plan.RequiresFullExtraction && plan.Reasons.Count > 0)
        {
            planMetadata["reasonCount"] = plan.Reasons.Count.ToString(CultureInfo.InvariantCulture);
        }

        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id,
            Stage = "refresh-plan",
            DocumentId = null,
            FieldPath = null,
            StartedAt = planTimestamp,
            FinishedAt = DateTime.UtcNow,
            Status = plan.Mode,
            Metadata = planMetadata
        }, ct).ConfigureAwait(false);

        HashSet<string>? fieldFilter = null;
        if (!plan.RequiresFullExtraction && plan.FieldsToExtract.Count > 0)
        {
            fieldFilter = plan.FieldsToExtract.ToHashSet(StringComparer.Ordinal);
        }

        var freshExtractions = new List<ExtractedField>();
        if (plan.RequiresFullExtraction || (fieldFilter?.Count ?? 0) > 0)
        {
            freshExtractions = await _fieldExtractor.ExtractAsync(pipeline, allPassages, _options, fieldFilter, ct).ConfigureAwait(false);
        }

        // STEP 2: Extract from Authoritative Notes (if present) - these have precedence=1 (highest)
        if (virtualDocumentId != null)
        {
            var notesExtractions = await _notesExtractor.ExtractFromNotesAsync(pipeline, virtualDocumentId, ct);
            _logger.LogInformation(
                "Extracted {Count} fields from Authoritative Notes for pipeline {PipelineId}",
                notesExtractions.Count,
                pipeline.Id);

            // Notes extractions are added FIRST (will take priority in merge)
            freshExtractions.InsertRange(0, notesExtractions);
        }

        var existingByField = existingFields.ToDictionary(e => e.FieldPath, StringComparer.Ordinal);
        var savedExtractions = new List<ExtractedField>();
        foreach (var extraction in freshExtractions)
        {
            extraction.FieldPath = FieldPathCanonicalizer.Canonicalize(extraction.FieldPath);

            if (existingByField.TryGetValue(extraction.FieldPath, out var prior))
            {
                extraction.Id = prior.Id;
            }

            extraction.UpdatedAt = DateTime.UtcNow;
            var saved = await extraction.Save(ct).ConfigureAwait(false);
            existingByField[extraction.FieldPath] = saved;
            savedExtractions.Add(saved);
        }

        List<ExtractedField> mergeCandidates;
        if (plan.RequiresFullExtraction)
        {
            mergeCandidates = savedExtractions.Count > 0 ? savedExtractions : existingFields.ToList();
        }
        else
        {
            var combined = new List<ExtractedField>(savedExtractions);
            var savedPaths = new HashSet<string>(savedExtractions.Select(f => f.FieldPath), StringComparer.Ordinal);

            foreach (var fieldPath in plan.FieldsToPreserve)
            {
                if (savedPaths.Contains(fieldPath))
                {
                    continue;
                }

                if (existingByField.TryGetValue(fieldPath, out var preserved))
                {
                    combined.Add(preserved);
                }
            }

            if (combined.Count == 0 && existingFields.Count > 0)
            {
                combined.AddRange(existingFields);
            }

            mergeCandidates = combined;
        }

        if (mergeCandidates.Count == 0)
        {
            if (existingFields.Count > 0)
            {
                mergeCandidates = existingFields.ToList();
                await _runLog.AppendAsync(new RunLog
                {
                    PipelineId = pipeline.Id,
                    Stage = "refresh-fallback",
                    FieldPath = null,
                    StartedAt = DateTime.UtcNow,
                    FinishedAt = DateTime.UtcNow,
                    Status = "preserved",
                    Metadata = new Dictionary<string, string>
                    {
                        ["reason"] = "no-new-extractions",
                        ["preservedFields"] = existingFields.Count.ToString(CultureInfo.InvariantCulture)
                    }
                }, ct).ConfigureAwait(false);
            }
            else
            {
                // Empty result is valid - log warning and complete successfully
                _logger.LogWarning("Pipeline {PipelineId} has no fields to merge (no extractions, no existing fields). Completing as empty.", pipeline.Id);
                mergeCandidates = new List<ExtractedField>();
            }
        }

        await _merger.MergeAsync(pipeline, mergeCandidates, ct);

        var completedAt = DateTime.UtcNow;
        pipeline.Status = PipelineStatus.Completed;
        pipeline.ProcessedDocuments = pipeline.TotalDocuments;
        pipeline.CompletedAt = completedAt;
        pipeline.UpdatedAt = completedAt;
        await pipeline.Save(ct).ConfigureAwait(false);

        // Move completed job to archive partition to preserve history and prevent reprocessing
        job.Status = JobStatus.Completed;
        job.CompletedAt = completedAt;
        job.HeartbeatAt = completedAt;
        job.ProcessedDocuments = job.TotalDocuments;
        
        _logger.LogInformation("Archiving completed job {JobId} to completed-jobs partition", job.Id);
        await job.Save("completed-jobs", ct).ConfigureAwait(false);
        
        // Remove from active jobs collection to prevent TryClaimAnyAsync from finding it
        await job.Delete(ct).ConfigureAwait(false);
        _logger.LogInformation("Job {JobId} archived and removed from active queue", job.Id);
    }

    /// <summary>
    /// Create a virtual document from Authoritative Notes to enable extraction via standard pipeline.
    /// Virtual documents have precedence=1 (highest priority) to override all other sources.
    /// </summary>
    private async Task<string> CreateVirtualDocumentFromNotesAsync(
        DocumentPipeline pipeline,
        CancellationToken ct)
    {
        var virtualDoc = new SourceDocument
        {
            PipelineId = pipeline.Id,
            OriginalFileName = "Authoritative Notes (User Override)",
            StorageKey = $"virtual-notes-{pipeline.Id}", // No actual file storage
            MediaType = "text/plain",
            Size = pipeline.AuthoritativeNotes?.Length ?? 0,
            IsVirtual = true,
            Precedence = 1, // Highest priority
            SourceType = MeridianConstants.SourceTypes.AuthoritativeNotes,
            ExtractedText = pipeline.AuthoritativeNotes ?? string.Empty,
            Status = DocumentProcessingStatus.Indexed, // Skip text extraction/classification
            ExtractionConfidence = 1.0,
            ExtractedAt = DateTime.UtcNow,
            ClassificationConfidence = 1.0,
            ClassificationMethod = ClassificationMethod.Manual,
            ClassificationReason = "Virtual document from Authoritative Notes",
            TextHash = TextExtractor.ComputeTextHash(pipeline.AuthoritativeNotes ?? string.Empty),
            PageCount = 1,
            UploadedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var saved = await virtualDoc.Save(ct).ConfigureAwait(false);

        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id,
            Stage = "virtual-document",
            DocumentId = saved.Id,
            FieldPath = null,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Status = "success",
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "Authoritative Notes",
                ["size"] = virtualDoc.Size.ToString()
            }
        }, ct);

        return saved.Id;
    }
}
