using Koan.Core;
using Koan.Core.Observability;
using Koan.Web.Connector.Swagger;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure Koan framework with web API support
builder.Services.AddKoan();

// Telemetry is enabled by referencing Koan.Observability (Reference=Intent, ARCH-0088).

// Swagger/OpenAPI is auto-registered via Koan.Web.Connector.Swagger
// Canon runtime and Customer pipeline are auto-registered via S8.Canon.Initialization.KoanAutoRegistrar

var app = builder.Build();

// Enable Swagger UI per policy: Dev by default; in non-dev only when Koan__Web__Swagger__Enabled=true
app.UseKoanSwagger();

app.RunAsync();

namespace S8.Canon
{
    public partial class Program { }
}
