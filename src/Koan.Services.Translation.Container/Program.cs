using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Services.Translation;
using Koan.Web;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Koan services (auto-discovers TranslationService via attribute)
builder.Services
    .AddKoan()
    .AsWebApi();

// Add controllers for HTTP endpoints
builder.Services.AddControllers();

var app = builder.Build();

AppHost.Current ??= app.Services;

// Map controllers for service endpoints
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "translation",
    timestamp = DateTime.UtcNow
}));

// Service manifest endpoint (RFC 8615 .well-known)
app.MapGet("/.well-known/koan-service", () => Results.Ok(new
{
    serviceId = "translation",
    displayName = "Translation Service",
    description = "AI-powered translation service supporting multiple languages",
    capabilities = new[] { "translate", "detect-language" },
    version = "0.1.0",
    endpoints = new
    {
        health = "/health",
        manifest = "/.well-known/koan-service",
        api = "/api/translation"
    }
}));

app.Run();
