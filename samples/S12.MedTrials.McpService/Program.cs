using Koan.AI.Web;
using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using S12.MedTrials;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsWebApi()
    .AsProxiedApi();

builder.Services.AddKoanObservability();
builder.Services.AddKoanDataVector();
builder.Services.AddKoanMcp(builder.Configuration);
builder.Services.AddKoanAiWeb();

builder.Services.AddMedTrialsCore();

var app = builder.Build();

app.UseCors();

app.Run();

namespace S12.MedTrials.McpService
{
    public partial class Program { }
}
