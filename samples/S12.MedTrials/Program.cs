using System.IO;
using Koan.AI.Web;
using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using Koan.Web.Swagger;
using S12.MedTrials.Infrastructure;
using S12.MedTrials.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddKoanObservability();
builder.Services.AddKoanDataVector();
builder.Services.AddKoanMcp(builder.Configuration);
builder.Services.AddKoanAiWeb();

builder.Services.AddScoped<IProtocolDocumentService, ProtocolDocumentService>();
builder.Services.AddScoped<IVisitPlanningService, VisitPlanningService>();
builder.Services.AddScoped<ISafetyDigestService, SafetyDigestService>();

builder.Services.AddHostedService<MedTrialsSeedWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    var dataPath = Path.Combine(app.Environment.ContentRootPath, "data");
    try { Directory.CreateDirectory(dataPath); } catch { /* best effort */ }
}

app.Run();

namespace S12.MedTrials
{
    public partial class Program { }
}
