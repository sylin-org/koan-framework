using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Web.Extensions;
using Koan.Web.Backup.Initialization;
using S5.Recs.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsProxiedApi()
    .WithRateLimit();

// Explicitly register parent relationship metadata service
builder.Services.AddSingleton<Koan.Data.Core.Relationships.IRelationshipMetadata, Koan.Data.Core.Relationships.RelationshipMetadataService>();

builder.Services.AddKoanObservability();
// Ensure local data folders exist for offline/bootstrap flows
Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(builder.Environment.ContentRootPath, S5.Recs.Infrastructure.Constants.Paths.OfflineData))!);
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, S5.Recs.Infrastructure.Constants.Paths.SeedCache));

// AI, vector, and data adapters are auto-registered by their modules via Koan.Core discovery

// Local services
builder.Services.AddMemoryCache();  // For sliding window cache
builder.Services.AddSingleton<IEmbeddingCache, EmbeddingCache>();
builder.Services.AddSingleton<ISeedService, SeedService>();
builder.Services.AddSingleton<IRecsService, RecsService>();
builder.Services.AddSingleton<S5.Recs.Services.Pagination.IBandCacheService, S5.Recs.Services.Pagination.BandCacheService>();
builder.Services.AddSingleton<IRecommendationSettingsProvider, RecommendationSettingsProvider>();
builder.Services.AddSingleton<IRawCacheService, RawCacheService>();
// Tag catalog options (censor list)
builder.Services.AddKoanOptions<S5.Recs.Options.TagCatalogOptions>(builder.Configuration, "S5:Recs:Tags");
// Scheduling: tasks are auto-discovered and registered by Koan.Scheduling's auto-registrar
// Scheduling defaults for S5: don't gate readiness; ensure bootstrap runs on startup
builder.Services.AddKoanOptions<Koan.Scheduling.SchedulingOptions>(builder.Configuration, "Koan:Scheduling", opts =>
{
    opts.ReadinessGate = false;
    if (!opts.Jobs.ContainsKey("s5:bootstrap"))
        opts.Jobs["s5:bootstrap"] = new Koan.Scheduling.SchedulingOptions.JobOptions { OnStartup = true };
});

// Discover and register all IMediaProvider implementations in this assembly
var providerInterface = typeof(S5.Recs.Providers.IMediaProvider);
var providerTypes = typeof(S5.Recs.Program).Assembly.GetTypes()
    .Where(t => providerInterface.IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
foreach (var t in providerTypes)
{
    builder.Services.AddSingleton(providerInterface, t);
}

// Discover and register all IMediaParser implementations
var parserInterface = typeof(IMediaParser);
var parserTypes = typeof(S5.Recs.Program).Assembly.GetTypes()
    .Where(t => parserInterface.IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
foreach (var t in parserTypes)
{
    builder.Services.AddSingleton(parserInterface, t);
}

// Register parser registry
builder.Services.AddSingleton<IMediaParserRegistry, MediaParserRegistry>();

// ═══════════════════════════════════════════════════════════════════════════
// ARCH-0069: Partition-Based Import Pipeline Workers
// ═══════════════════════════════════════════════════════════════════════════
builder.Services.AddSingleton<IImportOrchestrator, ImportOrchestrator>();
builder.Services.AddHostedService<S5.Recs.Services.Workers.ImportWorker>();
builder.Services.AddHostedService<S5.Recs.Services.Workers.ValidationWorker>();
// VectorizationWorker obsolete - embeddings now generated automatically via [Embedding] attribute (ARCH-0070)
//builder.Services.AddHostedService<S5.Recs.Services.Workers.VectorizationWorker>();
builder.Services.AddHostedService<S5.Recs.Services.Workers.CatalogWorker>();

// Couchbase adapter is auto-registered by its module via Koan.Core discovery

var app = builder.Build();

// Koan.Web startup filter auto-wires static files, controller routing, and Swagger

// Add authentication middleware
app.UseAuthentication();
app.UseAuthorization();

// Configure backup API endpoints (polling-based)
app.UseKoanWebBackupDevelopment();

// TestProvider endpoints are auto-mapped by its auto-registrar in Development.

app.Run();

namespace S5.Recs
{
    public partial class Program { }
}
