using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using S13.DocMind.Services;

namespace S13.DocMind.Infrastructure;

public sealed class DocMindRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "S13.DocMind";

    public string? ModuleVersion => typeof(DocMindRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<DocMindOptions>(DocMindOptions.Section);

        services.AddSingleton<IDocumentPipelineQueue, DocumentPipelineQueue>();
        services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
        services.AddScoped<IDocumentIntakeService, DocumentIntakeService>();
        services.AddScoped<ITextExtractionService, TextExtractionService>();
        services.AddScoped<IInsightSynthesisService, InsightSynthesisService>();
        services.AddScoped<ITemplateSuggestionService, TemplateSuggestionService>();
        services.AddScoped<IEmbeddingGenerator, EmbeddingGenerator>();
        services.AddSingleton(TimeProvider.System);

        services.AddHostedService<DocumentAnalysisPipeline>();
        services.AddHostedService<DocumentVectorBootstrapper>();
    }

    public void Describe(BootReport report, IConfiguration configuration, IHostEnvironment environment)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var docMindSection = configuration.GetSection(DocMindOptions.Section);
        report.AddSetting("Storage.BasePath", docMindSection[$"Storage:{nameof(DocMindOptions.StorageOptions.BasePath)}"] ?? "uploads");
        report.AddSetting("Processing.MaxConcurrency", docMindSection[$"Processing:{nameof(DocMindOptions.ProcessingOptions.MaxConcurrency)}"] ?? "auto");
        report.AddSetting("AI.DefaultModel", docMindSection[$"Ai:{nameof(DocMindOptions.AiOptions.DefaultModel)}"] ?? "llama3");
    }
}
