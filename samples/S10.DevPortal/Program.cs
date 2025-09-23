using Koan.Data.Core;
using Koan.Web.Extensions;
using S10.DevPortal.Services;

var builder = WebApplication.CreateBuilder(args);

// S10.DevPortal - Clean framework initialization demonstrating "Reference = Intent"
builder.Services.AddKoan()
    .AsWebApi()
    .AsProxiedApi();

// Local services for demo functionality
builder.Services.AddSingleton<IDemoSeedService, DemoSeedService>();

var app = builder.Build();

// Ensure local data folder exists for providers that default to ./data
if (app.Environment.IsDevelopment())
{
    var dataPath = Path.Combine(app.Environment.ContentRootPath, "data");
    try { Directory.CreateDirectory(dataPath); } catch { /* best effort */ }
}

app.Run();

// Make Program public for testing
namespace S10.DevPortal
{
    public partial class Program { }
}