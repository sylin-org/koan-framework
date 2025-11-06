using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSampleLogging();

// Framework bootstrap
builder.Services.AddKoan()
    // Toggle middleware
    .WithExceptionHandler()
    .WithRateLimit();

builder.Services.AddProblemDetails();

// Transformers disabled in S1: keep API simple here.

// Dev helper (optional): enable Debug console logging and allow DDL in a dev/prod shell
// Uncomment to see SQLite ensure/validate breadcrumbs (EventIds 1000-1200)
// builder.Logging.ClearProviders();
// builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
// builder.Logging.SetMinimumLevel(LogLevel.Debug);
// // Allow DDL in Production shell for local dev
// builder.Configuration["Koan:AllowMagicInProduction"] = "true";

// App policy: register a rate limiter (pipeline toggle enables UseRateLimiter)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Optional: use SQLite instead of JSON
// builder.Services.AddSqliteAdapter(o => o.ConnectionString = "Data Source=.\\data\\s1.sqlite");

// Auth: none in S1 sample (keep API simple)

// Koan.Web wires routing, controllers, static files, secure headers, and /api/health.
var app = builder.Build();

AppHost.Current ??= app.Services;

// Platform auto-ensures schema at startup when supported
if (app.Environment.IsDevelopment())
{
    // Ensure local data folder exists for providers that default to ./data
    var dataPath = Path.Combine(app.Environment.ContentRootPath, "data");
    try { Directory.CreateDirectory(dataPath); } catch { /* best effort */ }
}

app.ConfigureSampleLifecycle(
    sampleName: "S1 Web relationship demo",
    startupMessage: "S1 Web relationship demo running on {Addresses}. Close the window or press Ctrl+C to stop.",
    shutdownMessage: "S1 Web relationship demo shutting down.");

await app.RunAsync();

// Make Program public and partial to help WebApplicationFactory discovery in tests
namespace S1.Web
{
    public partial class Program { }
}