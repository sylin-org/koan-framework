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
            .ValidateOnStart();

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MeridianOptions>>().Value);

        // Core services
        services.AddSingleton(new DocumentStorageOptions());
        services.AddSingleton<IDocumentStorage, DocumentStorage>();
        services.AddSingleton(new DeliverableStorageOptions());
        services.AddSingleton<IDeliverableStorage, DeliverableStorage>();
        services.AddSingleton<IPdfRenderer, PdfRenderer>();
        services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();
        services.AddSingleton<IJobCoordinator, JobCoordinator>();
        services.AddSingleton<ITextExtractor, TextExtractor>();
        services.AddSingleton<IPassageChunker, PassageChunker>();
        services.AddSingleton<IPipelineAlertService, PipelineAlertService>();
        services.AddSingleton<IEmbeddingCache, EmbeddingCache>();
        services.AddSingleton<ISecureUploadValidator, SecureUploadValidator>();
        services.AddSingleton<IPassageIndexer, PassageIndexer>();
        services.AddSingleton<IFieldExtractor, FieldExtractor>();
        services.AddSingleton<IDocumentClassifier, DocumentClassifier>();
        services.AddSingleton<IRunLogWriter, RunLogWriter>();
        services.AddSingleton<IAiAssistAuditor, AiAssistAuditor>();
        services.AddSingleton<ISourceTypeAuthoringService, SourceTypeAuthoringService>();
        services.AddSingleton<IAnalysisTypeAuthoringService, AnalysisTypeAuthoringService>();
        services.AddSingleton<IModelHook<DocumentPipeline>, DocumentPipelineAnalysisTypeHook>();
        services.AddSingleton<IDocumentMerger, DocumentMerger>();
        services.AddSingleton<IPipelineProcessor, PipelineProcessor>();
        services.AddHostedService<ClassificationSeedService>();
        services.AddHostedService<MeridianJobWorker>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion, "Evidence-backed narrative pipeline sample.")
            .SetStatus("enabled");
    }
}
