using Sora.Data.Core;
using Sora.Core.Observability;
using Sora.Web;
using Sora.Web.Swagger;
using Sora.Web.Transformers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .WithRateLimit();

// Optional: enable OpenTelemetry based on config/env (Sora:Observability or OTEL_* env vars)
builder.Services.AddSoraObservability();
// Optional: add Swagger/OpenAPI (Dev on; non-dev gated by flags). Needed for /swagger UI.
builder.Services.AddSoraSwagger(builder.Configuration);

// Enable Mongo adapter via discovery or explicit registration (optional)
// builder.Services.AddMongoAdapter();

var app = builder.Build();
// Enable Swagger UI per policy: Dev by default; in non-dev only when Sora__Web__Swagger__Enabled=true or SORA_MAGIC_ENABLE_SWAGGER=true
app.UseSoraSwagger();
app.Run();

public partial class Program { }
