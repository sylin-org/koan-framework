using Koan.Core;
using Koan.Core.Observability;
using Koan.Web.Connector.Swagger;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure Koan framework with web API support
builder.Services.AddKoan();

// Telemetry is enabled by referencing Koan.Observability (Reference=Intent, ARCH-0088).

// Swagger/OpenAPI is activated by the Koan.Web.Connector.Swagger reference.
// Canon runtime and the Customer pipeline are activated by CanonSampleModule.

var app = builder.Build();

// Enable Swagger UI per policy: Dev by default; in non-dev only when Koan__Web__Swagger__Enabled=true
app.UseKoanSwagger();

await app.RunAsync();

namespace CustomerCanon
{
    public partial class Program { }
}
