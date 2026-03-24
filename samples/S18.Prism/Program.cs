using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web;
using Koan.Web.Extensions;
using Koan.ZenGarden;
using Koan.ZenGarden.Extensions;
using S18.Prism.Initialization;
using S18.Prism.Services;
using S18.Prism.Services.Extraction;
using S18.Prism.Services.SourcePulling;

[assembly: KoanApp(Name = "Prism", Code = "prism", Description = "Personal Knowledge Intelligence")]

var builder = WebApplication.CreateBuilder(args);

// Koan Framework - "Reference = Intent"
builder.Services
    .AddKoan()
    .AsWebApi();
builder.Services.AddKoanZenGarden(builder.Configuration);

// Content extractors (ordered by Priority)
builder.Services.AddScoped<IContentExtractor, TextExtractor>();
builder.Services.AddScoped<IContentExtractor, AiFallbackExtractor>();

// Application services
builder.Services.AddScoped<INoteIngestionService, NoteIngestionService>();
builder.Services.AddScoped<IPulseService, PulseService>();

// Source pull adapters
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ISourcePullAdapter, RssPullAdapter>();
builder.Services.AddSingleton<ISourcePullAdapter, HackerNewsPullAdapter>();
builder.Services.AddSingleton<ISourcePullAdapter, GitHubPullAdapter>();
builder.Services.AddSingleton<ISourcePullAdapter, FolderWatchPullAdapter>();
builder.Services.AddSingleton<ISourcePullAdapter, WebPullAdapter>();

// Background workers
builder.Services.AddHostedService<SourcePullWorker>();
builder.Services.AddHostedService<ResearchBriefWorker>();
builder.Services.AddHostedService<ModelCrawlerWorker>();

// SignalR for real-time updates
builder.Services.AddSignalR();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5087", "http://127.0.0.1:5087")
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();
AppHost.Current ??= app.Services;

// Seed default spaces
var logger = app.Services.GetRequiredService<ILogger<Program>>();
await SpaceSeeder.SeedDefaults(logger);

app.UseCors();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.RunAsync();
