using Koan.Mcp;
using Koan.Mcp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan(options =>
{
    options.EnableMcp = true;
    options.McpTransports = McpTransports.Stdio | McpTransports.HttpSse;
})
.AddKoanMcp();

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.MapKoanMcpEndpoints();

app.Run();
