using Sora.Core.Observability;
using Sora.Core;
using Sora.Data.Core;
using Sora.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddSoraObservability();
// Ensure local data folders exist for offline/bootstrap flows
Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(builder.Environment.ContentRootPath, S5.Recs.Infrastructure.Constants.Paths.OfflineData))!);
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, S5.Recs.Infrastructure.Constants.Paths.SeedCache));

// AI, Ollama, and Weaviate are auto-registered by their modules via Sora.Core discovery

// Local services
builder.Services.AddSingleton<S5.Recs.Services.ISeedService, S5.Recs.Services.SeedService>();
builder.Services.AddSingleton<S5.Recs.Services.IRecsService, S5.Recs.Services.RecsService>();
builder.Services.AddSingleton<S5.Recs.Services.IRecommendationSettingsProvider, S5.Recs.Services.RecommendationSettingsProvider>();
// Tag catalog options (censor list)
builder.Services.AddSoraOptions<S5.Recs.Options.TagCatalogOptions>(builder.Configuration, "S5:Recs:Tags");
// Scheduling: tasks are auto-discovered and registered by Sora.Scheduling's auto-registrar
// Scheduling defaults for S5: don't gate readiness; ensure bootstrap runs on startup
builder.Services.AddSoraOptions<Sora.Scheduling.SchedulingOptions>(builder.Configuration, "Sora:Scheduling", opts =>
{
    opts.ReadinessGate = false;
    if (!opts.Jobs.ContainsKey("s5:bootstrap"))
        opts.Jobs["s5:bootstrap"] = new Sora.Scheduling.SchedulingOptions.JobOptions { OnStartup = true };
});

// Discover and register all IAnimeProvider implementations in this assembly
var providerInterface = typeof(S5.Recs.Providers.IAnimeProvider);
var providerTypes = typeof(S5.Recs.Program).Assembly.GetTypes()
    .Where(t => providerInterface.IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
foreach (var t in providerTypes)
{
    builder.Services.AddSingleton(providerInterface, t);
}

// Mongo adapter is auto-registered by its module via Sora.Core discovery

var app = builder.Build();

// Sora.Web startup filter auto-wires static files, controller routing, and Swagger

app.Run();

namespace S5.Recs
{
    public partial class Program { }
}
