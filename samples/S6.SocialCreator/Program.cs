using Sora.Data.Core;

var builder = WebApplication.CreateBuilder(args);

// Sora bootstrap
builder.Services.AddSora();

var app = builder.Build();

// Ensure controllers are mapped (Sora.Web may disable AutoMapControllers via config)
app.MapControllers();

// Static files and default files are enabled by Sora.Web
app.Run();

// Expose Program for WebApplicationFactory in integration tests
public partial class Program { }