using Sora.Core.Observability;
using Sora.Data.Core;
using Sora.Web;
using Sora.Web.Swagger;
using Sora.Web.Transformers;

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

var app = builder.Build();
// Enable Swagger UI per policy: Dev by default; in non-dev only when Sora__Web__Swagger__Enabled=true or SORA_MAGIC_ENABLE_SWAGGER=true
app.UseSoraSwagger();
app.Run();

public partial class Program { }
