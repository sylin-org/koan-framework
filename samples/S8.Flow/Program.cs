using Sora.Data.Core; // AddSora()
using Sora.Flow;      // AddSoraFlow(), AddSoraFlowWeb()
using Sora.Flow.Options;
using Sora.Testing.Flow;

var builder = WebApplication.CreateBuilder(args);

// Sora base setup (core + data + web)
builder.Services.AddSora();

// Flow capabilities with ubiquitous keys
builder.Services.AddSoraFlow();
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
