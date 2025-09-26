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
        services.AddKoanOptions<DocMindStorageOptions>(DocMindStorageOptions.Section);
        services.AddKoanOptions<DocMindProcessingOptions>(DocMindProcessingOptions.Section);
        services.AddKoanOptions<DocMindAiOptions>(DocMindAiOptions.Section);

        services.AddSingleton<DocumentPipelineQueue>();
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

        var storageSection = configuration.GetSection(DocMindStorageOptions.Section);
        report.AddSetting("Storage.BasePath", storageSection[nameof(DocMindStorageOptions.BasePath)] ?? "uploads");
        report.AddSetting("Processing.MaxConcurrency", configuration[$"{DocMindProcessingOptions.Section}:{nameof(DocMindProcessingOptions.MaxConcurrency)}"] ?? "auto");
        report.AddSetting("AI.DefaultModel", configuration[$"{DocMindAiOptions.Section}:{nameof(DocMindAiOptions.DefaultModel)}"] ?? "llama3");
    }
}
