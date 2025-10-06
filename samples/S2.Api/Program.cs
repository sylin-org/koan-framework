using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Messaging;
using Koan.Web.Extensions;
using Koan.Web.Extensions.GenericControllers;
using Koan.Web.Connector.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsProxiedApi()
    .WithRateLimit();

// Optional: enable OpenTelemetry based on config/env (Koan:Observability or OTEL_* env vars)
builder.Services.AddKoanObservability();
// Swagger/OpenAPI is auto-registered via Koan.Web.Connector.Swagger. Call AddKoanSwagger only for custom config.

// Enable Mongo adapter via discovery or explicit registration (optional)
// builder.Services.AddMongoAdapter();

// Wire messaging core + RabbitMQ so diagnostics surface is populated when used
// [REMOVED obsolete AddMessagingCore usage]
// RabbitMQ registers via auto-registrar when the assembly is referenced; no explicit AddRabbitMq needed

// Register generic capability controllers for Item under api/items
builder.Services
    .AddEntityAuditController<S2.Api.Controllers.Item>("api/items")
    .AddEntitySoftDeleteController<S2.Api.Controllers.Item, string>("api/items")
    .AddEntityModerationController<S2.Api.Controllers.Item, string>("api/items");

var app = builder.Build();
// Enable Swagger UI per policy: Dev by default; in non-dev only when Koan__Web__Swagger__Enabled=true or Koan_MAGIC_ENABLE_SWAGGER=true
app.UseKoanSwagger();
app.Run();

namespace S2.Api
{
    public partial class Program { }
}

