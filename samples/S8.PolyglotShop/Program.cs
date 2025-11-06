using Koan.AI.Connector.Ollama;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Koan (auto-discovers all services including Translation)
builder.Services
    .AddKoan()
    .AsWebApi();

builder.Services.AddOllamaFromConfig();
builder.Services.AddControllers();

var app = builder.Build();

AppHost.Current ??= app.Services;

// Serve static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Map API controllers
app.MapControllers();

app.Run();
