using Aspire.Hosting;
using Koan.Orchestration.Aspire.Extensions;

// Set required environment variables for Aspire dashboard before any initialization
Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:15888");
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", "http://localhost:4317");
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

// CRITICAL: Set Development environment for "Reference = Intent" to work
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

var builder = DistributedApplication.CreateBuilder(args);

// Create infrastructure resources that Koan modules will use
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var redis = builder.AddRedis("redis")
    .WithDataVolume();

// Optionally configure enhanced provider selection
builder.UseKoanProviderSelection("auto"); // Uses Koan's intelligent provider detection

// Add the sample application and wire it to the infrastructure
// WithReference automatically injects connection strings as environment variables
var app = builder.AddProject("koan-aspire-sample", "../KoanAspireIntegration/KoanAspireIntegration.csproj")
    .WithReference(postgres)
    .WithReference(redis);

// Build and run the Aspire application
var aspireApp = builder.Build();
await aspireApp.RunAsync();