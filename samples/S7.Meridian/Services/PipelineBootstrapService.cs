using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

/// <summary>
/// Service for creating pipelines from file-only payloads with embedded configuration.
/// </summary>
public interface IPipelineBootstrapService
{
    /// <summary>
    /// Creates a pipeline from uploaded files containing analysis-config.json and documents.
    /// </summary>
    Task<CreatePipelineResponse> CreateFromFilesAsync(
        IFormFileCollection files,
        CancellationToken ct = default
    );
}

/// <summary>
/// Implementation of pipeline bootstrap service using file-only payload approach.
/// </summary>
public class PipelineBootstrapService : IPipelineBootstrapService
{
    private readonly ITypeCodeResolver _typeCodeResolver;
    private readonly IDocumentClassifier _documentClassifier;
    private readonly IDocumentIngestionService _ingestionService;
    private readonly ILogger<PipelineBootstrapService> _logger;

    private const string ConfigFileName = "analysis-config.json";

    public PipelineBootstrapService(
        ITypeCodeResolver typeCodeResolver,
        IDocumentClassifier documentClassifier,
        IDocumentIngestionService ingestionService,
        ILogger<PipelineBootstrapService> logger)
    {
        _typeCodeResolver = typeCodeResolver;
        _documentClassifier = documentClassifier;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    public async Task<CreatePipelineResponse> CreateFromFilesAsync(
        IFormFileCollection files,
        CancellationToken ct = default)
    {
        // 1. Find and parse config file
        var configFile = files.FirstOrDefault(f =>
            f.FileName.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase));

        if (configFile == null)
        {
            throw new InvalidOperationException(
                $"Required file '{ConfigFileName}' not found in upload. " +
                "Please include a configuration file with pipeline and analysis definitions.");
        }

        AnalysisConfig config;
        try
        {
            using var configStream = configFile.OpenReadStream();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            config = await JsonSerializer.DeserializeAsync<AnalysisConfig>(configStream, options, ct)
                ?? throw new InvalidOperationException("Configuration file is empty or invalid");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in {ConfigFileName}: {ex.Message}", ex);
        }

        // 2. Validate configuration
        await ValidateConfigAsync(config, ct);

        // 3. Resolve or create analysis type
        var (analysisType, isCustom) = await ResolveAnalysisTypeAsync(config.Analysis, ct);

        // 4. Create pipeline
        var pipeline = new DocumentPipeline
        {
            Name = config.Pipeline.Name,
            Description = config.Pipeline.Description,
            AuthoritativeNotes = config.Pipeline.Notes, // Maps to AuthoritativeNotes property
            BiasNotes = config.Pipeline.Bias, // Maps to BiasNotes property
            AnalysisTypeId = analysisType.Id,
            AnalysisTypeVersion = analysisType.Version,
            // Copy schema and template from analysis type for field extraction
            SchemaJson = analysisType.JsonSchema,
            AnalysisInstructions = analysisType.Instructions,
            TemplateMarkdown = analysisType.OutputTemplate,
            AnalysisTags = analysisType.Tags,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await pipeline.Save(ct);
        _logger.LogInformation("Created pipeline {PipelineId} '{PipelineName}'",
            pipeline.Id, pipeline.Name);

        // 5. Process documents (excluding config file)
        var documentFiles = files.Where(f =>
            !f.FileName.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (documentFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one document is required (excluding analysis-config.json)");
        }

        var documentResults = new List<DocumentCreationResult>();
        int manifestCount = 0;
        int autoClassifiedCount = 0;

        foreach (var file in documentFiles)
        {
            var result = await ProcessDocumentAsync(
                pipeline,
                file,
                config.Manifest,
                ct);

            documentResults.Add(result);

            if (result.InManifest)
                manifestCount++;
            else
                autoClassifiedCount++;
        }

        // Save pipeline with attached document IDs
        await pipeline.Save(ct);

        // 6. Create processing job
        var job = new ProcessingJob
        {
            PipelineId = pipeline.Id,
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        job.MergeDocuments(documentResults.Select(d => d.DocumentId));

        await job.Save(ct);
        _logger.LogInformation("Created job {JobId} for pipeline {PipelineId}",
            job.Id, pipeline.Id);

        // 7. Build response
        return new CreatePipelineResponse
        {
            PipelineId = pipeline.Id,
            PipelineName = pipeline.Name,
            AnalysisType = isCustom ? null : analysisType.Code,
            AnalysisTypeName = analysisType.Name,
            IsCustomAnalysis = isCustom,
            JobId = job.Id,
            Status = job.Status.ToString(),
            Documents = documentResults,
            Statistics = new PipelineCreationStatistics
            {
                TotalDocuments = documentResults.Count,
                ManifestSpecified = manifestCount,
                AutoClassified = autoClassifiedCount
            }
        };
    }

    private async Task ValidateConfigAsync(AnalysisConfig config, CancellationToken ct)
    {
        // Validate pipeline config
        if (string.IsNullOrWhiteSpace(config.Pipeline.Name))
        {
            throw new InvalidOperationException("Pipeline name is required");
        }

        // Validate analysis definition
        var analysisValidation = config.Analysis.Validate();
        if (!analysisValidation.IsValid)
        {
            throw new InvalidOperationException(analysisValidation.ErrorMessage);
        }

        // Validate analysis type code if specified
        if (!string.IsNullOrWhiteSpace(config.Analysis.Type))
        {
            var availableCodes = await _typeCodeResolver.GetAvailableAnalysisCodesAsync(ct);
            if (!availableCodes.Contains(config.Analysis.Type, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Analysis type '{config.Analysis.Type}' not found. " +
                    $"Available codes: {string.Join(", ", availableCodes)}");
            }
        }

        // Validate manifest source type codes
        if (config.Manifest.Count > 0)
        {
            var availableSourceCodes = await _typeCodeResolver.GetAvailableSourceCodesAsync(ct);
            foreach (var entry in config.Manifest)
            {
                if (!availableSourceCodes.Contains(entry.Value.Type, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Source type '{entry.Value.Type}' not found in manifest for file '{entry.Key}'. " +
                        $"Available codes: {string.Join(", ", availableSourceCodes)}");
                }
            }
        }
    }

    private async Task<(AnalysisType Type, bool IsCustom)> ResolveAnalysisTypeAsync(
        AnalysisDefinition definition,
        CancellationToken ct)
    {
        // Seeded type
        if (!string.IsNullOrWhiteSpace(definition.Type))
        {
            var type = await _typeCodeResolver.ResolveAnalysisTypeAsync(definition.Type, ct);
            if (type == null)
            {
                var availableCodes = await _typeCodeResolver.GetAvailableAnalysisCodesAsync(ct);
                throw new InvalidOperationException(
                    $"Analysis type '{definition.Type}' not found. " +
                    $"Available codes: {string.Join(", ", availableCodes)}");
            }

            return (type, false);
        }

        // Custom type - create ephemeral analysis type
        var customType = new AnalysisType
        {
            Name = definition.Name!,
            Description = $"Custom analysis: {definition.Name}",
            Instructions = definition.Instructions!,
            OutputTemplate = definition.Template!,
            JsonSchema = definition.Schema?.ToString() ?? "{}",
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await customType.Save(ct);
        _logger.LogInformation("Created custom analysis type {TypeId} '{TypeName}'",
            customType.Id, customType.Name);

        return (customType, true);
    }

    private async Task<DocumentCreationResult> ProcessDocumentAsync(
        DocumentPipeline pipeline,
        IFormFile file,
        Dictionary<string, ManifestEntry> manifest,
        CancellationToken ct)
    {
        // Check if file is in manifest
        _logger.LogDebug("Processing document: {FileName}. Manifest has {Count} entries: {Keys}",
            file.FileName, manifest.Count, string.Join(", ", manifest.Keys));

        bool inManifest = manifest.TryGetValue(file.FileName, out var manifestEntry);

        _logger.LogDebug("Document {FileName} inManifest={InManifest}, manifestEntry={Entry}",
            file.FileName, inManifest, manifestEntry?.Type ?? "null");

        SourceType? sourceType;
        string method;
        double confidence;
        int typeVersion;

        if (inManifest && manifestEntry != null)
        {
            // Manual classification from manifest
            sourceType = await _typeCodeResolver.ResolveSourceTypeAsync(manifestEntry.Type, ct);
            if (sourceType == null)
            {
                var availableCodes = await _typeCodeResolver.GetAvailableSourceCodesAsync(ct);
                throw new InvalidOperationException(
                    $"Source type '{manifestEntry.Type}' specified in manifest for '{file.FileName}' not found. " +
                    $"Available codes: {string.Join(", ", availableCodes)}");
            }

            method = "Manual";
            confidence = 1.0;
            typeVersion = sourceType.Version;
        }
        else
        {
            // Auto-classify using existing DocumentClassifier
            // First, create a temporary SourceDocument for classification
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(ct);

            var tempDocument = new SourceDocument
            {
                OriginalFileName = file.FileName,
                ExtractedText = content,
                MediaType = file.ContentType,
                Size = file.Length,
                PageCount = 1, // Estimate - will be updated during ingestion
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var classification = await _documentClassifier.ClassifyAsync(tempDocument, ct);

            sourceType = await SourceType.Get(classification.TypeId, ct);
            method = classification.Method.ToString();
            confidence = classification.Confidence;
            typeVersion = classification.Version;
        }

        // Create the actual document entity (document-centric, not tied to pipeline)
        var document = new SourceDocument
        {
            OriginalFileName = file.FileName,
            MediaType = file.ContentType,
            Size = file.Length,
            ClassifiedTypeId = sourceType?.Id ?? string.Empty,
            ClassifiedTypeVersion = typeVersion,
            ClassificationMethod = Enum.Parse<ClassificationMethod>(method, ignoreCase: true),
            ClassificationConfidence = confidence,
            ClassificationReason = inManifest ? $"Manually specified in manifest" : $"Auto-classified via {method}",
            UploadedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await document.Save(ct);

        // Add document to pipeline's document list
        pipeline.AttachDocument(document.Id);

        // Store document content via ingestion service
        // Note: Ingestion service will create its own document with auto-classification,
        // but we keep OUR document which has the correct manifest-based classification
        var ingestionResult = await _ingestionService.IngestAsync(pipeline.Id, file, forceReprocess: false, ct);

        // If ingestion created a different document, copy its storage info and remove it
        var ingestedDoc = ingestionResult.NewDocuments.FirstOrDefault() ?? ingestionResult.ReusedDocuments.FirstOrDefault();
        if (ingestedDoc != null && ingestedDoc.Id != document.Id)
        {
            // Copy storage key and other ingestion details from ingested document
            document.StorageKey = ingestedDoc.StorageKey;
            document.ContentHash = ingestedDoc.ContentHash;
            document.ExtractedText = ingestedDoc.ExtractedText;
            document.PageCount = ingestedDoc.PageCount;
            await document.Save(ct);

            // Remove the auto-classified document created by ingestion service
            pipeline.DocumentIds.Remove(ingestedDoc.Id);
            _logger.LogDebug("Copied storage key from ingested document {DupId} to manifest-classified document {CorrectId}, removed duplicate",
                ingestedDoc.Id, document.Id);
        }

        _logger.LogInformation(
            "Created document {DocumentId} '{FileName}' for pipeline {PipelineId} " +
            "(Source: {SourceType}, Method: {Method}, Confidence: {Confidence:P0}, InManifest: {InManifest})",
            document.Id, file.FileName, pipeline.Id,
            sourceType?.Code ?? "UNKNOWN", method, confidence, inManifest);

        return new DocumentCreationResult
        {
            DocumentId = document.Id,
            FileName = file.FileName,
            SourceType = sourceType?.Code ?? "UNKNOWN",
            SourceTypeName = sourceType?.Name ?? "Unknown",
            Method = method,
            Confidence = confidence,
            InManifest = inManifest
        };
    }
}
