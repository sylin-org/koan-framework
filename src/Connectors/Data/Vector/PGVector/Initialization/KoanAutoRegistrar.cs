using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Connector.PGVector.Initialization;

/// <summary>
/// Auto-registration for PGVector connector.
/// Activated by package reference (Reference = Intent pattern).
/// Registers IVectorAdapterFactory, options, and extension manager.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.PGVector";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register PGVector options
        services.AddKoanOptions<PGVectorOptions>(Infrastructure.ConfigurationConstants.Section);

        // Register extension manager as singleton (shared state for version detection)
        services.AddSingleton<PgVectorExtensionManager>();

        // Register vector adapter factory
        services.AddSingleton<IVectorAdapterFactory, PGVectorAdapterFactory>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Provider: pgvector (Vector pillar)");
        module.AddNote("Indexing: HNSW, IVFFlat");
        module.AddNote("Metrics: Cosine, L2, InnerProduct");
        module.AddNote("Metadata filtering: JSONB");
        module.AddNote($"Max dimension: {PGVectorOptions.MaxDimension}");
    }
}
