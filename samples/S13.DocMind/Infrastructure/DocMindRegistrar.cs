using System.Globalization;
using System.IO;
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
        services.AddSingleton<IValidateOptions<DocMindOptions>, DocMindOptionsValidator>();

        services.AddSingleton<IDocumentPipelineQueue, DocumentPipelineQueue>();
        services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
        services.AddScoped<IDocumentProcessingEventSink, DocumentProcessingEventRepositorySink>();
        services.AddScoped<IDocumentIntakeService, DocumentIntakeService>();
        services.AddScoped<ITextExtractionService, TextExtractionService>();
        services.AddScoped<IVisionInsightService, VisionInsightService>();
        services.AddScoped<IInsightSynthesisService, InsightSynthesisService>();
        services.AddScoped<ITemplateSuggestionService, TemplateSuggestionService>();
        services.AddScoped<IEmbeddingGenerator, EmbeddingGenerator>();
        services.AddScoped<IDocumentInsightsService, DocumentInsightsService>();
        services.AddScoped<IDocumentAggregationService, DocumentAggregationService>();
        services.AddScoped<IDocumentProcessingDiagnostics, DocumentProcessingDiagnostics>();
        services.AddSingleton(TimeProvider.System);

        services.AddHostedService<DocumentAnalysisPipeline>();
        services.AddHostedService<DocumentVectorBootstrapper>();
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

        report.AddSetting("Processing.QueueCapacity", options.Processing.QueueCapacity.ToString(CultureInfo.InvariantCulture));
        report.AddSetting("Processing.MaxConcurrency", options.Processing.MaxConcurrency.ToString(CultureInfo.InvariantCulture));
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
    }
}
