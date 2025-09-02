using Sora.Data.Core; // AddSora()
using Sora.Flow;      // Turnkey via Sora.Flow.Web (auto AddSoraFlow)
using Sora.Flow.Options;
using Sora.Testing.Flow;

var builder = WebApplication.CreateBuilder(args);

// Sora base setup (core + data + web)
builder.Services.AddSora();

// Flow runtime is auto-registered by Sora.Flow.Web (turnkey ON by default). Set Sora:Flow:AutoRegister=false to opt out.
builder.Services.Configure<FlowOptions>(o =>
{
    o.AggregationTags = FlowTestConstants.UbiquitousAggregationTags;
    o.BatchSize = 50;
    o.PurgeEnabled = true;
    o.PurgeInterval = TimeSpan.FromMinutes(30);
});

// Controllers (Flow.Web controllers discovered by assembly reference)
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();
