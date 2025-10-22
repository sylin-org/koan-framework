using Koan.Core;

var builder = WebApplication.CreateBuilder(args);

// Koan bootstrap
builder.Services.AddKoan();

var app = builder.Build();

// Ensure controllers are mapped (Koan.Web may disable AutoMapControllers via config)
app.MapControllers();

// Static files and default files are enabled by Koan.Web
app.Run();

// Expose Program for WebApplicationFactory in integration tests
public partial class Program { }