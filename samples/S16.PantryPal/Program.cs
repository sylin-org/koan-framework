using Koan.Data.Core;
using Koan.Mcp.Extensions;
using Koan.Web.Extensions;
using S16.PantryPal.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsWebApi();

builder.Services.AddKoanMcp(builder.Configuration);
builder.Services.AddPantryVision();

var app = builder.Build();

app.UseStaticFiles();
app.MapKoanMcpEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

namespace S16.PantryPal
{
    public partial class Program { }
}
