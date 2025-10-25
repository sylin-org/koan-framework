using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
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
    private readonly IDocumentIngestionService _ingestionService;
    private readonly ILogger<PipelineBootstrapService> _logger;

    private const string ConfigFileName = "analysis-config.json";

    public PipelineBootstrapService(
        ITypeCodeResolver typeCodeResolver,
        IDocumentIngestionService ingestionService,
        ILogger<PipelineBootstrapService> logger)
    {
        _typeCodeResolver = typeCodeResolver;
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
            {
                manifestCount++;
            }
            else
            {
                autoClassifiedCount++;
            }
        }

        // Save pipeline with attached document IDs
        await pipeline.Save(ct);

        // 6. Create processing job
        var job = new ProcessingJob
        {
            PipelineId = pipeline.Id,
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TotalDocuments = documentResults.Count,
            ProcessedDocuments = 0
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

        // Store the document once. Ingestion service handles attachment and dedupe.
            var ingestionResult = await _ingestionService.IngestAsync(pipeline.Id, file, forceReprocess: false, typeHint: null, ct);
        var storedDocument = ingestionResult.NewDocuments.FirstOrDefault()
            ?? ingestionResult.ReusedDocuments.FirstOrDefault();

        if (storedDocument == null)
        {
            throw new InvalidOperationException($"Unable to ingest document '{file.FileName}'.");
        }

        SourceType? sourceType = null;
        string method;
        double confidence;

        if (inManifest && manifestEntry != null)
        {
            sourceType = await _typeCodeResolver.ResolveSourceTypeAsync(manifestEntry.Type, ct);
            if (sourceType == null)
            {
                var availableCodes = await _typeCodeResolver.GetAvailableSourceCodesAsync(ct);
                throw new InvalidOperationException(
                    $"Source type '{manifestEntry.Type}' specified in manifest for '{file.FileName}' not found. " +
                    $"Available codes: {string.Join(", ", availableCodes)}");
            }

            storedDocument.SourceType = sourceType.Code;
            storedDocument.ClassifiedTypeId = sourceType.Id;
            storedDocument.ClassifiedTypeVersion = sourceType.Version;
            storedDocument.ClassificationMethod = ClassificationMethod.Manual;
            storedDocument.ClassificationConfidence = 1.0;
            storedDocument.ClassificationReason = "Specified in manifest";
            storedDocument.UpdatedAt = DateTime.UtcNow;
            await storedDocument.Save(ct);

            method = "Manual";
            confidence = 1.0;
        }
        else
        {
            // Leave ingestion defaults (unclassified) for background processing.
            method = "Deferred";
            confidence = 0.0;

            if (string.IsNullOrWhiteSpace(storedDocument.SourceType))
            {
                storedDocument.SourceType = MeridianConstants.SourceTypes.Unclassified;
                storedDocument.UpdatedAt = DateTime.UtcNow;
                await storedDocument.Save(ct);
            }
        }

        _logger.LogInformation(
            "Registered document {DocumentId} '{FileName}' for pipeline {PipelineId} " +
            "(Source: {SourceType}, Method: {Method}, InManifest: {InManifest})",
            storedDocument.Id, file.FileName, pipeline.Id,
            sourceType?.Code ?? storedDocument.SourceType ?? "Unclassified",
            method, inManifest);

        var documentSourceType = sourceType?.Code ?? storedDocument.SourceType ?? MeridianConstants.SourceTypes.Unclassified;
        var resolvedSourceTypeName = sourceType?.Name;

        if (string.IsNullOrWhiteSpace(resolvedSourceTypeName))
        {
            if (inManifest)
            {
                resolvedSourceTypeName = "Manifest";
            }
            else if (string.Equals(documentSourceType, MeridianConstants.SourceTypes.Unclassified, StringComparison.OrdinalIgnoreCase))
            {
                resolvedSourceTypeName = "Pending Classification";
            }
            else
            {
                resolvedSourceTypeName = documentSourceType;
            }
        }

        return new DocumentCreationResult
        {
            DocumentId = storedDocument.Id!,
            FileName = file.FileName,
            SourceType = documentSourceType,
            SourceTypeName = resolvedSourceTypeName,
            Method = method,
            Confidence = confidence,
            InManifest = inManifest
        };
    }
}
