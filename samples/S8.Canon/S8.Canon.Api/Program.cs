using Koan.Core;
using Koan.Core.Observability;
using Koan.Web.Connector.Swagger;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();
// Telemetry is enabled by referencing Koan.Observability (Reference=Intent, ARCH-0088).

var app = builder.Build();

app.UseKoanSwagger();
app.RunAsync();

namespace S8.Canon.Api
{
    // Marker for integration tests
    public partial class Program { }
}