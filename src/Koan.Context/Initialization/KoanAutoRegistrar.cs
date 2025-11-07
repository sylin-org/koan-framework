using Koan.Context.Services;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Context.Initialization;

/// <summary>
/// Auto-registrar for Koan.Context services
/// </summary>
/// <remarks>
/// Registers all ingest pipeline services following Koan's auto-discovery pattern.
/// Services are registered as scoped to allow per-request isolation.
/// </remarks>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Context";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register ingest pipeline services
        services.AddScoped<IDocumentDiscoveryService, DocumentDiscoveryService>();
        services.AddScoped<IContentExtractionService, ContentExtractionService>();
        services.AddScoped<IChunkingService, ChunkingService>();
        services.AddScoped<IEmbeddingService>(sp =>
        {
            var ai = sp.GetRequiredService<Koan.AI.Contracts.IAi>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmbeddingService>>();

            // TODO: Get default model from configuration
            var defaultModel = "all-minilm";

            return new EmbeddingService(ai, cache, logger, defaultModel);
        });
        services.AddScoped<IIndexingService, IndexingService>();
        services.AddScoped<IRetrievalService, RetrievalService>();

        // Register Phase 1 AI-first services
        services.AddSingleton<ITokenCountingService, TokenCountingService>();
        services.AddSingleton<IContinuationTokenService, ContinuationTokenService>();
        services.AddSingleton<ISourceUrlGenerator, SourceUrlGenerator>();

        // Add memory cache if not already registered
        services.AddMemoryCache();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Ingest pipeline: Discovery, Extraction, Chunking, Embedding, Indexing");
    }
}
