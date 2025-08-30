using Sora.Core.Observability;
using Sora.Data.Core;
using Sora.Messaging;
using Sora.Web.Extensions;
using Sora.Web.Extensions.GenericControllers;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

// Optional: enable OpenTelemetry based on config/env (Sora:Observability or OTEL_* env vars)
builder.Services.AddSoraObservability();
// Swagger/OpenAPI is auto-registered via Sora.Web.Swagger. Call AddSoraSwagger only for custom config.

// Enable Mongo adapter via discovery or explicit registration (optional)
// builder.Services.AddMongoAdapter();

// Wire messaging core + RabbitMQ so diagnostics surface is populated when used
builder.Services.AddMessagingCore();
// RabbitMQ registers via auto-registrar when the assembly is referenced; no explicit AddRabbitMq needed

// Register generic capability controllers for Item under api/items
builder.Services
    .AddEntityAuditController<S2.Api.Controllers.Item>("api/items")
    .AddEntitySoftDeleteController<S2.Api.Controllers.Item, string>("api/items")
    .AddEntityModerationController<S2.Api.Controllers.Item, string>("api/items");

var app = builder.Build();
// Enable Swagger UI per policy: Dev by default; in non-dev only when Sora__Web__Swagger__Enabled=true or SORA_MAGIC_ENABLE_SWAGGER=true
app.UseSoraSwagger();
app.Run();

namespace S2.Api
{
    public partial class Program { }
}
