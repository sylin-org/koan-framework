using System;
using Koan.Core;
using Koan.Core.Provenance;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Samples.Meridian.Models;
using Koan.Web.Hooks;

namespace Koan.Samples.Meridian.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Meridian";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Configuration options
        services.AddOptions<MeridianOptions>()
            .BindConfiguration("Meridian")
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .PostConfigure(options => options.NormalizeFieldPaths());

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MeridianOptions>>().Value);

        // Core services
        services.AddSingleton(new DocumentStorageOptions());
        services.AddSingleton<IDocumentStorage, DocumentStorage>();
        services.AddSingleton(new DeliverableStorageOptions());
        services.AddSingleton<IDeliverableStorage, DeliverableStorage>();
        services.AddHttpClient<IPdfRenderer, PandocPdfRenderer>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MeridianOptions>>().Value;
            var pandoc = options.Rendering.Pandoc;
            if (!string.IsNullOrWhiteSpace(pandoc.BaseUrl))
            {
                client.BaseAddress = new Uri(pandoc.BaseUrl, UriKind.Absolute);
            }

            var timeout = Math.Clamp(pandoc.TimeoutSeconds, 5, 600);
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();
        services.AddSingleton<IJobCoordinator, JobCoordinator>();
        services.AddHttpClient<IOcrClient, TesseractOcrClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MeridianOptions>>().Value;
            var baseUrl = options.Extraction.Ocr.BaseUrl;
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            }

            var timeout = Math.Clamp(options.Extraction.Ocr.TimeoutSeconds, 5, 300);
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });

        services.AddSingleton<ITextExtractor, TextExtractor>();
        services.AddSingleton<IPassageChunker, PassageChunker>();
        services.AddSingleton<IPipelineAlertService, PipelineAlertService>();
        services.AddSingleton<IEmbeddingCache, EmbeddingCache>();
        services.AddSingleton<ISecureUploadValidator, SecureUploadValidator>();
        services.AddSingleton<IPassageIndexer, PassageIndexer>();
        services.AddSingleton<INotesExtractionService, NotesExtractionService>();
        services.AddSingleton<IIncrementalRefreshPlanner, IncrementalRefreshPlanner>();
        services.AddSingleton<IDocumentClassifier, DocumentClassifier>();
        services.AddSingleton<IDocumentFactExtractor, DocumentFactExtractor>();
        services.AddSingleton<IFieldFactMatcher, FieldFactMatcher>();
        services.AddSingleton<IRunLogWriter, RunLogWriter>();
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
        services.AddSingleton<IAiAssistAuditor, AiAssistAuditor>();
        services.AddSingleton<ISourceTypeAuthoringService, SourceTypeAuthoringService>();
        services.AddSingleton<IAnalysisTypeAuthoringService, AnalysisTypeAuthoringService>();
        services.AddSingleton<IModelHook<AnalysisType>, AnalysisTypeCanonicalizationHook>();
        services.AddSingleton<IModelHook<DeliverableType>, DeliverableTypeCanonicalizationHook>();
        services.AddSingleton<IModelHook<DocumentPipeline>, DocumentPipelineAnalysisTypeHook>();
        services.AddSingleton<IDocumentMerger, DocumentMerger>();
        services.AddSingleton<IPipelineProcessor, PipelineProcessor>();
        services.AddSingleton<ITypeCodeResolver, TypeCodeResolver>();
        services.AddSingleton<IPipelineBootstrapService, PipelineBootstrapService>();
        services.AddHostedService<MeridianJobWorker>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion, "Evidence-backed narrative pipeline sample.")
            .SetStatus("enabled");
    }
}
