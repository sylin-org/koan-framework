using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Core;
using Koan.Mcp.Extensions;
using S13.DocMind.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

AssemblyCache.Instance.AddAssembly(typeof(DocMindRegistrar).Assembly);

builder.Services.AddKoan();
builder.Services.AddKoanMcp(builder.Configuration);
builder.Services
    .AddKoanOptions<DocMindOptions>(builder.Configuration, DocMindOptions.Section)
    .ValidateOnStart();

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.MapKoanMcpEndpoints();

app.Run();
