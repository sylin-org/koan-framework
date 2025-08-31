using Sora.Core.Hosting;
using Sora.Flow;
using Sora.Flow.Options;
using Sora.Testing.Flow;

var builder = WebApplication.CreateBuilder(args);

// Sora base setup
builder.Services.AddSora(builder.Configuration, app =>
{
    app.UseSwagger();
    app.UseDataCore();
    app.UseWeb();
});

// Data persistence (JSON for demo simplicity)
builder.Services.AddSoraDataCore();

// Flow capabilities with ubiquitous keys
builder.Services.AddSoraFlow();
builder.Services.Configure<FlowOptions>(o =>
{
    o.AggregationTags = FlowTestConstants.UbiquitousAggregationTags;
    o.BatchSize = 50;
    o.PurgeEnabled = true;
    o.PurgeInterval = TimeSpan.FromMinutes(30);
});

// Flow Web API endpoints
builder.Services.AddSoraFlowWeb();

var app = builder.Build();

app.UseSora();

app.Run();
