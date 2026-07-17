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

// Ollama (Koan.AI.Connector.Ollama) and Swagger (Koan.Web.Connector.Swagger) are wired by
// Reference = Intent — their modules register the services and the Swagger startup filter mounts
// the UI in development. No explicit AddOllamaFromConfig()/UseSwagger() calls are needed.

var app = builder.Build();

AppHost.Current ??= app.Services;

app.UseAuthorization();
app.MapControllers();

app.RunAsync();
public partial class Program { }
