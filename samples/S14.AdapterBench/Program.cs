using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Core;
using Koan.Web.Extensions;
using S14.AdapterBench.Services;

var builder = WebApplication.CreateBuilder(args);

// Koan framework auto-registration
builder.Services.AddKoan()
    .AsWebApi();

// SignalR for real-time benchmark progress
builder.Services.AddSignalR();

// Benchmark service
builder.Services.AddSingleton<IBenchmarkService, BenchmarkService>();

var app = builder.Build();

// Koan.Web startup filter auto-wires static files, controller routing, and Swagger

// Map SignalR hub
app.MapHub<S14.AdapterBench.Hubs.BenchmarkHub>("/hubs/benchmark");

app.Run();

namespace S14.AdapterBench
{
    public partial class Program { }
}
