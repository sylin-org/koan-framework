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
/// Stateless services (Extraction, TokenCounter, etc.) are singletons.
/// Stateful services (Indexer, Search) are scoped for per-request isolation.
/// </remarks>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Context";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register stateless pipeline services (singleton - configuration only)
        services.AddSingleton<Extraction>();

        // Register stateful pipeline services (scoped - per-request)
        services.AddScoped<Discovery>();
        services.AddScoped<Chunker>();
        services.AddScoped<Embedding>(sp =>
        {
            var ai = sp.GetRequiredService<Koan.AI.Contracts.IAi>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Embedding>>();

            // TODO: Get default model from configuration
            var defaultModel = "all-minilm";

            return new Embedding(ai, cache, logger, defaultModel);
        });
        services.AddScoped<Indexer>();
        services.AddScoped<Search>();

        // Register Phase 1 AI-first services
        services.AddSingleton<TokenCounter>();
        services.AddSingleton<Pagination>();
        services.AddSingleton<UrlBuilder>();

        // Register background services
        services.AddHostedService<VectorSyncWorker>();

        // Add memory cache if not already registered
        services.AddMemoryCache();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Ingest pipeline: Discovery, Extraction, Chunking, Embedding, Indexing");
        module.AddNote("Transactional Outbox: VectorSyncWorker (at-least-once delivery)");
    }
}
