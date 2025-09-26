using Koan.AI.Web;
using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Data.Mongo;
using Koan.Data.Vector;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using Koan.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddKoanObservability();
builder.Services.AddKoanDataVector();
builder.Services.AddKoanAiWeb();
builder.Services.AddKoanSwagger(builder.Configuration);
builder.Services.AddKoanMcp(builder.Configuration);
builder.Services.AddMongoAdapter();

var app = builder.Build();

app.UseKoanSwagger();

app.Run();

namespace S13.DocMind
{
    public partial class Program;
}
