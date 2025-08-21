using Sora.Core.Observability;
using Sora.Data.Core;
using Sora.Data.Mongo;
using Sora.Web;
using Sora.Web.Swagger;
using Sora.AI;
using Sora.Ai.Provider.Ollama;
using Sora.Data.Weaviate;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddSoraObservability();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// AI core + Ollama provider (auto-discovers endpoints in Dev; configurable via Sora:Ai)
builder.Services.AddAi(builder.Configuration);
builder.Services.AddOllamaFromConfig();
builder.Services.AddOptions<WeaviateOptions>()
    .BindConfiguration("Sora:Data:Weaviate")
    .PostConfigure(opts =>
    {
        if (string.IsNullOrWhiteSpace(opts.Endpoint))
        {
            var inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
            // Prefer container DNS when inside compose; otherwise host-exposed port per OPS-0014
            opts.Endpoint = inContainer ? "http://weaviate:8080" : "http://localhost:5082";
        }
    });

// Local services
builder.Services.AddSingleton<S5.Recs.Services.ISeedService, S5.Recs.Services.SeedService>();
builder.Services.AddSingleton<S5.Recs.Services.IRecsService, S5.Recs.Services.RecsService>();

// Discover and register all IAnimeProvider implementations in this assembly
var providerInterface = typeof(S5.Recs.Providers.IAnimeProvider);
var providerTypes = typeof(Program).Assembly.GetTypes()
    .Where(t => providerInterface.IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
foreach (var t in providerTypes)
{
    builder.Services.AddSingleton(providerInterface, t);
}

// Optional Mongo for document storage; reads defaults from env/config
builder.Services.AddMongoAdapter();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSoraSwagger();

app.MapControllers();

app.Run();

public partial class Program { }
