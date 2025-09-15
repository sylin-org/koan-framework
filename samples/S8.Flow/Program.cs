using Koan.Data.Core; // AddKoan()
using Koan.Flow;      // Turnkey via Koan.Flow.Web (auto AddKoanFlow)
using Koan.Flow.Options;
using Koan.Testing.Flow;

var builder = WebApplication.CreateBuilder(args);

// Koan base setup (core + data + web)
builder.Services.AddKoan();

// Flow runtime is auto-registered by Koan.Flow.Web (turnkey ON by default). Set Koan:Flow:AutoRegister=false to opt out.
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
