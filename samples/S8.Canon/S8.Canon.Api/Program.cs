using Koan.Web.Connector.Swagger;
using Koan.Web.Extensions;
using Koan.Core.Observability;
using Koan.Data.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();
builder.Services.AddKoanObservability();

var app = builder.Build();

app.UseKoanSwagger();
app.Run();

namespace S8.Canon.Api
{
    // Marker for integration tests
    public partial class Program { }
}