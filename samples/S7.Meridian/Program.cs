using Koan.AI.Connector.Ollama;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web;
using Koan.Web.Extensions;


var builder = WebApplication.CreateBuilder(args);

// Koan Framework Bootstrap
builder.Services
    .AddKoan()
    .AsWebApi();

builder.Services.AddOllamaFromConfig();

var app = builder.Build();

AppHost.Current ??= app.Services;

// Koan Environment Info
if (KoanEnv.IsDevelopment)
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Meridian API v1"));
}

app.UseAuthorization();
app.MapControllers();

app.Run();
public partial class Program { }
