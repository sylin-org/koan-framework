using System.Globalization;
using System.IO;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Core;
using Koan.Data.Vector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.Infrastructure;

public sealed class DocMindRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "S13.DocMind";

    public string? ModuleVersion => typeof(DocMindRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<DocMindOptions>(DocMindOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DocMindOptions>, DocMindOptionsValidator>();

        services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
        services.AddScoped<IDocumentProcessingEventSink, DocumentProcessingEventRepositorySink>();
        services.AddScoped<IDocumentIntakeService, DocumentIntakeService>();
        services.AddScoped<ITextExtractionService, TextExtractionService>();
        services.AddScoped<IVisionInsightService, VisionInsightService>();
        services.AddScoped<IInsightSynthesisService, InsightSynthesisService>();
        services.AddScoped<IManualAnalysisService, ManualAnalysisService>();
        services.AddSingleton<IDocMindPromptBuilder, DocMindPromptBuilder>();
        services.AddScoped<ITemplateSuggestionService, TemplateSuggestionService>();
        services.AddScoped<IEmbeddingGenerator, EmbeddingGenerator>();
        services.AddScoped<IDocumentInsightsService, DocumentInsightsService>();
        services.AddScoped<IDocumentProcessingDiagnostics, DocumentProcessingDiagnostics>();
        services.AddSingleton<IDocumentDiscoveryRefresher, DocumentDiscoveryRefresher>();
        services.AddSingleton<DocMindVectorHealth>();
        services.AddSingleton<DocumentDiscoveryRefreshService>();
        services.AddSingleton<IDocumentDiscoveryRefreshScheduler>(sp => sp.GetRequiredService<DocumentDiscoveryRefreshService>());
        services.AddSingleton(TimeProvider.System);

        // AI Model Management Services
        services.AddSingleton<IModelCatalogService, InMemoryModelCatalogService>();
        services.AddSingleton<IModelInstallationQueue, InMemoryModelInstallationQueue>();

        services.AddHostedService<DocumentProcessingWorker>();
        services.AddHostedService<DocumentVectorBootstrapper>();
        // Temporarily comment out ModelInstallationBackgroundService to test
        // services.AddHostedService<ModelInstallationBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<DocumentDiscoveryRefreshService>());

        services.AddHealthChecks()
            .AddCheck<DocMindStorageHealthCheck>("docmind_storage")
            .AddCheck<DocMindVectorHealthCheck>("docmind_vector")
            .AddCheck<DocMindDiscoveryHealthCheck>("docmind_discovery");
    }

    public void Describe(BootReport report, IConfiguration configuration, IHostEnvironment environment)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var docMindSection = configuration.GetSection(DocMindOptions.Section);
        var options = docMindSection.Get<DocMindOptions>() ?? new DocMindOptions();

        report.AddSetting("Storage.BasePath", options.Storage.BasePath);
        report.AddSetting("Storage.Bucket", options.Storage.Bucket);
        report.AddSetting("Storage.AllowedContentTypes", string.Join(",", options.Storage.AllowedContentTypes));

        var physicalPath = Path.IsPathRooted(options.Storage.BasePath)
            ? options.Storage.BasePath
            : Path.Combine(environment.ContentRootPath, options.Storage.BasePath);
        if (!Directory.Exists(physicalPath))
        {
            report.AddNote($"Storage path missing: {physicalPath}");
        }

        report.AddSetting("Processing.MaxConcurrency", options.Processing.MaxConcurrency.ToString(CultureInfo.InvariantCulture));
        report.AddSetting("Processing.WorkerBatchSize", options.Processing.WorkerBatchSize.ToString(CultureInfo.InvariantCulture));
        report.AddSetting("Processing.PollIntervalSeconds", options.Processing.PollIntervalSeconds.ToString(CultureInfo.InvariantCulture));
        report.AddSetting("Processing.MaxRetryAttempts", options.Processing.MaxRetryAttempts.ToString(CultureInfo.InvariantCulture));

        report.AddSetting("AI.DefaultModel", options.Ai.DefaultModel);
        report.AddSetting("AI.EmbeddingModel", options.Ai.EmbeddingModel);
        report.AddSetting("AI.VisionModel", options.Ai.VisionModel ?? "(disabled)");

        var vectorStatus = Vector<DocumentChunkEmbedding>.IsAvailable ? "available" : "unavailable";
        report.AddSetting("Vector.Adapter", vectorStatus);
        if (!Vector<DocumentChunkEmbedding>.IsAvailable)
        {
            report.AddNote("Vector adapter not detected; semantic suggestions will fallback to lexical scoring.");
        }

        var vectorSnapshot = DocMindVectorHealth.LatestSnapshot;
        report.AddSetting("Vector.FallbackActive", vectorSnapshot.FallbackActive.ToString());
        if (vectorSnapshot.MissingProfiles.Count > 0)
        {
            report.AddNote("Missing semantic profile vectors: " + string.Join(", ", vectorSnapshot.MissingProfiles));
        }
        if (!string.IsNullOrWhiteSpace(vectorSnapshot.LastAuditError))
        {
            report.AddNote("Vector audit error: " + vectorSnapshot.LastAuditError);
        }

        var discoveryStatus = DocumentDiscoveryRefreshService.LatestStatus;
        report.AddSetting("Discovery.Pending", discoveryStatus.PendingCount.ToString(CultureInfo.InvariantCulture));
        report.AddSetting("Discovery.TotalCompleted", discoveryStatus.TotalCompleted.ToString(CultureInfo.InvariantCulture));
        report.AddSetting("Discovery.TotalFailed", discoveryStatus.TotalFailed.ToString(CultureInfo.InvariantCulture));
        if (discoveryStatus.AverageDuration is not null)
        {
            report.AddSetting("Discovery.AverageDurationMs", discoveryStatus.AverageDuration.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
        }
        if (discoveryStatus.MaxDuration is not null)
        {
            report.AddSetting("Discovery.MaxDurationMs", discoveryStatus.MaxDuration.Value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(discoveryStatus.LastError))
        {
            report.AddNote("Discovery refresh error: " + discoveryStatus.LastError);
        }
    }
}
