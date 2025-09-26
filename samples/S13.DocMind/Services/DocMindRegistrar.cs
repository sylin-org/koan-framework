using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using S13.DocMind.Infrastructure;

namespace S13.DocMind.Services;

public static class DocMindRegistrar
{
    private const string QueueSection = "S13:DocMind:Processing:Queue";
    private const string PipelineSection = "S13:DocMind:Processing:Pipeline";

    public static IServiceCollection AddDocMindProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentPipelineQueueOptions>(configuration.GetSection(QueueSection));
        services.Configure<DocumentAnalysisOptions>(configuration.GetSection(PipelineSection));

        services.AddSingleton<IDocumentPipelineQueue, DocumentPipelineQueue>();
        services.AddSingleton<IDocumentProcessingEventSink, DocumentProcessingEventLogger>();
        services.AddSingleton<ITemplateGeneratorService, TemplateGeneratorService>();
        services.AddSingleton<IVisionInsightService, VisionInsightService>();
        services.AddSingleton<IInsightAggregationService, InsightAggregationService>();
        services.AddSingleton<DocumentAnalysisPipeline>();
        services.AddHostedService<DocumentProcessingHostedService>();

        return services;
    }
}
