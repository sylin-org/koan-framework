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
    private readonly IFactCatalogBuilder _catalogBuilder;
    private readonly IFactCategorizer _categorizer;
    private readonly ISchemaGuidedExtractor _extractor;
    private readonly IFieldConflictResolver _conflictResolver;
    private readonly IDocumentMerger _merger;
    private readonly IDocumentClassifier _classifier;
    private readonly IDocumentStyleClassifier _styleClassifier;
    private readonly IRunLogWriter _runLog;
    private readonly IIncrementalRefreshPlanner _refreshPlanner;
    private readonly MeridianOptions _options;
    private readonly ILogger<PipelineProcessor> _logger;

    public PipelineProcessor(
        ITextExtractor textExtractor,
        IPassageChunker chunker,
        IPassageIndexer indexer,
        IFactCatalogBuilder catalogBuilder,
        IFactCategorizer categorizer,
        ISchemaGuidedExtractor extractor,
        IFieldConflictResolver conflictResolver,
        IDocumentMerger merger,
        IDocumentClassifier classifier,
        IDocumentStyleClassifier styleClassifier,
        IRunLogWriter runLog,
        IIncrementalRefreshPlanner refreshPlanner,
        MeridianOptions options,
        ILogger<PipelineProcessor> logger)
    {
        _textExtractor = textExtractor;
        _chunker = chunker;
        _indexer = indexer;
        _catalogBuilder = catalogBuilder;
        _categorizer = categorizer;
        _extractor = extractor;
        _conflictResolver = conflictResolver;
        _merger = merger;
        _classifier = classifier;
        _styleClassifier = styleClassifier;
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
        pipeline.AttachDocuments(job.DocumentIds);
        pipeline.Status = PipelineStatus.Processing;
        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct);

        // STEP 1: Create virtual document from Authoritative Notes (if present)
        string? virtualDocumentId = null;
        if (!string.IsNullOrWhiteSpace(pipeline.AuthoritativeNotes))
        {
            virtualDocumentId = await CreateVirtualDocumentFromNotesAsync(pipeline, ct);
            if (!string.IsNullOrWhiteSpace(virtualDocumentId))
            {
                pipeline.AttachDocument(virtualDocumentId);
                pipeline.UpdatedAt = DateTime.UtcNow;
                await pipeline.Save(ct);
            }
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
                job.ProcessedDocuments = processedCount;
                job.LastDocumentId = documentId;
                job.HeartbeatAt = DateTime.UtcNow;
                await job.Save(ct);
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
                job.ProcessedDocuments = processedCount;
                job.LastDocumentId = document.Id;
                job.HeartbeatAt = DateTime.UtcNow;
                await job.Save(ct);
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

            document.Status = DocumentProcessingStatus.Classified;
            document.UpdatedAt = classifyFinished;
            await document.Save(ct);

            changedDocuments.Add(document.Id);

            var allowedMetadata = new Dictionary<string, string>
            {
                ["typeId"] = classification.TypeId,
                ["confidence"] = classification.Confidence.ToString("0.00"),
                ["method"] = classification.Method.ToString(),
                ["reason"] = classification.Reason
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

            // Check if document type should skip processing (e.g., test files, non-documents)
            var sourceType = await SourceType.Get(classification.TypeId, ct);
            if (sourceType?.SkipProcessing == true)
            {
                _logger.LogInformation(
                    "Document {DocumentId} classified as {TypeName} with SkipProcessing=true ({Reason}). Skipping chunking and extraction.",
                    document.Id,
                    sourceType.Name,
                    classification.Reason);

                document.Status = DocumentProcessingStatus.Indexed; // Mark as processed
                document.UpdatedAt = DateTime.UtcNow;
                await document.Save(ct);

                processedCount++;
                job.ProcessedDocuments = processedCount;
                job.LastDocumentId = document.Id;
                job.HeartbeatAt = DateTime.UtcNow;
                await job.Save(ct);
                continue; // Skip to next document
            }

            var chunks = _chunker.Chunk(document, extraction.Text);
            foreach (var chunk in chunks)
            {
                var saved = await chunk.Save(ct);
                passages.Add(saved);
            }

            processedCount++;
            job.ProcessedDocuments = processedCount;
            job.LastDocumentId = document.Id;
            job.HeartbeatAt = DateTime.UtcNow;
            await job.Save(ct);
        }

        await _indexer.IndexAsync(pipeline.Id, passages, ct);

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

        var allPassages = await pipeline.LoadPassagesAsync(ct);
        var existingFields = (await ExtractedField.Query(e => e.PipelineId == pipeline.Id, ct))
            .Where(field => string.Equals(field.PipelineId, pipeline.Id, StringComparison.Ordinal))
            .Select(field =>
            {
                field.FieldPath = FieldPathCanonicalizer.Canonicalize(field.FieldPath);
                return field;
            })
            .ToList();
        var plan = await _refreshPlanner.PlanAsync(pipeline, changedDocuments, existingFields, ct);

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
        }, ct);

        HashSet<string>? fieldFilter = null;
        if (!plan.RequiresFullExtraction && plan.FieldsToExtract.Count > 0)
        {
            fieldFilter = plan.FieldsToExtract
                .Select(FieldPathCanonicalizer.Canonicalize)
                .ToHashSet(StringComparer.Ordinal);
        }

        var freshExtractions = new List<ExtractedField>();
        if (plan.RequiresFullExtraction || (fieldFilter?.Count ?? 0) > 0)
        {
            var analysisType = await AnalysisType.Get(pipeline.AnalysisTypeId, ct)
                ?? throw new InvalidOperationException($"AnalysisType '{pipeline.AnalysisTypeId}' not found for pipeline {pipeline.Id}.");

            // STAGE 1: Build complete fact catalog from org profile + analysis type
            var factCatalog = await _catalogBuilder.BuildAsync(pipeline, analysisType, ct);

            // STAGE 2: Semantic categorization of facts into contextual batches (cached)
            var categorizationMap = await _categorizer.CategorizeAsync(factCatalog, analysisType, ct);

            // STAGE 3: Targeted extraction per batch per document
            var allExtractedFields = new List<ExtractedField>();

            foreach (var docId in pipeline.DocumentIds.Distinct(StringComparer.Ordinal))
            {
                var document = await SourceDocument.Get(docId, ct);
                if (document is null)
                {
                    _logger.LogWarning("Unable to load document {DocumentId} for fact extraction", docId);
                    continue;
                }

                // STAGE 2.5: Document style classification (cached if already classified)
                await _styleClassifier.ClassifyAsync(document, ct);

                foreach (var batch in categorizationMap.Batches)
                {
                    // Apply field filter if specified
                    var batchToExtract = batch;
                    if (fieldFilter != null)
                    {
                        var filteredBatch = new SemanticBatch
                        {
                            BatchId = batch.BatchId,
                            CategoryName = batch.CategoryName,
                            CategoryDescription = batch.CategoryDescription,
                            FieldPaths = batch.FieldPaths
                                .Where(fp => fieldFilter.Contains(fp))
                                .ToList()
                        };

                        if (filteredBatch.FieldPaths.Count == 0)
                        {
                            continue; // Skip batch if no fields match filter
                        }

                        batchToExtract = filteredBatch;
                    }

                    var batchFields = await _extractor.ExtractBatchAsync(
                        document, batchToExtract, factCatalog, pipeline.Id, ct);
                    allExtractedFields.AddRange(batchFields);
                }
            }

            // STAGE 3.5: Extract from authoritative notes (privileged source)
            if (!string.IsNullOrWhiteSpace(pipeline.AuthoritativeNotes))
            {
                _logger.LogInformation("Extracting from authoritative notes for pipeline {PipelineId}", pipeline.Id);

                foreach (var batch in categorizationMap.Batches)
                {
                    // Apply field filter if specified
                    var batchToExtract = batch;
                    if (fieldFilter != null)
                    {
                        var filteredBatch = new SemanticBatch
                        {
                            BatchId = batch.BatchId,
                            CategoryName = batch.CategoryName,
                            CategoryDescription = batch.CategoryDescription,
                            FieldPaths = batch.FieldPaths
                                .Where(fp => fieldFilter.Contains(fp))
                                .ToList()
                        };

                        if (filteredBatch.FieldPaths.Count == 0)
                        {
                            continue;
                        }

                        batchToExtract = filteredBatch;
                    }

                    var notesBatchFields = await _extractor.ExtractBatchFromNotesAsync(
                        pipeline.AuthoritativeNotes, batchToExtract, factCatalog, pipeline.Id, ct);
                    allExtractedFields.AddRange(notesBatchFields);
                }
            }

            // STAGE 4: Conflict resolution (Notes precedence: 1, Documents precedence: 3)
            freshExtractions = _conflictResolver.Resolve(allExtractedFields);

            _logger.LogInformation(
                "Schema-guided extraction complete: {TotalFields} fields extracted, {ResolvedFields} after conflict resolution",
                allExtractedFields.Count, freshExtractions.Count);
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
            var saved = await extraction.Save(ct);
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
                }, ct);
            }
            else
            {
                // Empty result is valid - log warning and complete successfully
                _logger.LogWarning("Pipeline {PipelineId} has no fields to merge (no extractions, no existing fields). Completing as empty.", pipeline.Id);
                mergeCandidates = new List<ExtractedField>();
            }
        }

        await _merger.MergeAsync(pipeline, mergeCandidates, ct);

        // Update all documents with the extraction version to detect stale extractions on reuse
        if (!string.IsNullOrWhiteSpace(pipeline.AnalysisTypeId))
        {
            var analysisType = await AnalysisType.Get(pipeline.AnalysisTypeId, ct);
            if (analysisType != null)
            {
                foreach (var docId in pipeline.DocumentIds.Distinct(StringComparer.Ordinal))
                {
                    var document = await SourceDocument.Get(docId, ct);
                    if (document != null && !document.IsVirtual)
                    {
                        document.LastExtractedAnalysisTypeVersion = analysisType.Version;
                        document.UpdatedAt = DateTime.UtcNow;
                        await document.Save(ct);
                    }
                }

                _logger.LogInformation(
                    "Updated {Count} documents with AnalysisType version {Version} for pipeline {PipelineId}",
                    pipeline.DocumentIds.Count, analysisType.Version, pipeline.Id);
            }
        }

        var completedAt = DateTime.UtcNow;
        pipeline.Status = PipelineStatus.Completed;
        pipeline.CompletedAt = completedAt;
        pipeline.UpdatedAt = completedAt;
        await pipeline.Save(ct);

        // Move completed job to archive partition to preserve history and prevent reprocessing
        job.Status = JobStatus.Completed;
        job.CompletedAt = completedAt;
        job.HeartbeatAt = completedAt;
        job.ProcessedDocuments = job.TotalDocuments;

        _logger.LogInformation("Archiving completed job {JobId} to completed-jobs partition", job.Id);
        await job.Save("completed-jobs", ct);

        // Remove from active jobs collection to prevent TryClaimAnyAsync from finding it
        await job.Delete(ct);
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
        var notesContent = pipeline.AuthoritativeNotes ?? string.Empty;
        var notesHash = TextExtractor.ComputeTextHash(notesContent);

        // Check for existing virtual document attached to this pipeline
        var existingVirtual = (await SourceDocument.Query(
            d => d.IsVirtual && d.SourceType == MeridianConstants.SourceTypes.AuthoritativeNotes,
            ct))
            .FirstOrDefault(d => pipeline.DocumentIds.Contains(d.Id!));

        if (existingVirtual != null)
        {
            // Update content if changed
            if (existingVirtual.TextHash != notesHash)
            {
                _logger.LogInformation("Updating existing virtual document {DocId} with new notes (hash changed)", existingVirtual.Id);
                existingVirtual.ExtractedText = notesContent;
                existingVirtual.ContentHash = notesHash;
                existingVirtual.TextHash = notesHash;
                existingVirtual.Size = notesContent.Length;
                existingVirtual.UpdatedAt = DateTime.UtcNow;
                existingVirtual.ExtractedAt = DateTime.UtcNow;
                await existingVirtual.Save(ct);

                await _runLog.AppendAsync(new RunLog
                {
                    PipelineId = pipeline.Id,
                    Stage = "virtual-document-update",
                    DocumentId = existingVirtual.Id,
                    FieldPath = null,
                    StartedAt = DateTime.UtcNow,
                    FinishedAt = DateTime.UtcNow,
                    Status = "success",
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "Authoritative Notes (Updated)",
                        ["size"] = existingVirtual.Size.ToString()
                    }
                }, ct);
            }
            else
            {
                _logger.LogDebug("Reusing existing virtual document {DocId} (no changes)", existingVirtual.Id);
            }

            return existingVirtual.Id;
        }

        // Create new virtual document
        var virtualDoc = new SourceDocument
        {
            OriginalFileName = "Authoritative Notes (User Override)",
            StorageKey = $"virtual-notes-{pipeline.Id}", // No actual file storage
            MediaType = "text/plain",
            Size = notesContent.Length,
            ContentHash = notesHash,
            IsVirtual = true,
            Precedence = 1, // Highest priority
            SourceType = MeridianConstants.SourceTypes.AuthoritativeNotes,
            ExtractedText = notesContent,
            Status = DocumentProcessingStatus.Indexed, // Skip text extraction/classification
            ExtractionConfidence = 1.0,
            ExtractedAt = DateTime.UtcNow,
            ClassificationConfidence = 1.0,
            ClassificationMethod = ClassificationMethod.Manual,
            ClassificationReason = "Virtual document from Authoritative Notes",
            TextHash = notesHash,
            PageCount = 1,
            UploadedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var saved = await virtualDoc.Save(ct);

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
