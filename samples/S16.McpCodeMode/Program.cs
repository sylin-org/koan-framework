using Koan.Data.Core;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsWebApi();

builder.Services.AddKoanMcp(builder.Configuration);

var app = builder.Build();

app.MapKoanMcpEndpoints();

app.Run();

namespace S16.McpCodeMode
{
    public partial class Program { }
}
