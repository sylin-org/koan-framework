using Koan.Data.Core; // AddKoan()
using Koan.Canon;      // Turnkey via Koan.Canon.Web (auto AddKoanCanon)
using Koan.Canon.Options;
using Koan.Testing.Flow;

var builder = WebApplication.CreateBuilder(args);

// Koan base setup (core + data + web)
builder.Services.AddKoan();

// Canon runtime is auto-registered by Koan.Canon.Web (turnkey ON by default). Set Koan:Canon:AutoRegister=false to opt out.
builder.Services.Configure<CanonOptions>(o =>
{
    o.AggregationTags = FlowTestConstants.UbiquitousAggregationTags;
    o.BatchSize = 50;
    o.PurgeEnabled = true;
    o.PurgeInterval = TimeSpan.FromMinutes(30);
});

// Controllers (Canon.Web controllers discovered by assembly reference)
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();


