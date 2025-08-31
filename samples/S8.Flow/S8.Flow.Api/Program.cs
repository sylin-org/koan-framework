using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Data.Core;
using Sora.Flow;
using Sora.Flow.Options;
using S8.Flow.Shared;
using Sora.Messaging;
using S8.Flow.Api.Adapters;

var builder = WebApplication.CreateBuilder(args);

// Container-only sample guard
if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Api is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.AddSora(); // core + data + health + scheduling
builder.Services.AddSoraFlow();

builder.Services.Configure<FlowOptions>(o =>
{
    o.AggregationTags = new[] { Keys.Device.Inventory, Keys.Device.Serial };
    o.PurgeEnabled = false;
});

builder.Services.AddControllers();
builder.Services.AddRouting();

// Message-driven adapters: handle TelemetryEvent and persist to Flow intake
builder.Services.OnMessages(h =>
{
    h.On<TelemetryEvent>(async (env, msg) =>
    {
        var payload = msg.ToPayloadDictionary();
        var rec = new Sora.Flow.Model.Record
        {
            RecordId = Guid.NewGuid().ToString("n"),
            SourceId = msg.Source,
            OccurredAt = msg.CapturedAt,
            StagePayload = payload
        };
        await rec.Save(Sora.Flow.Infrastructure.Constants.Sets.Intake);
    });
});

// Health registry stub (adapters now live out-of-process)
builder.Services.AddSingleton<IAdapterHealthRegistry, NullAdapterHealthRegistry>();

var app = builder.Build();

// Ensure ambient AppHost is set and runtime started before hosted services execute
Sora.Core.Hosting.App.AppHost.Current = app.Services;
try { Sora.Core.SoraEnv.TryInitialize(app.Services); } catch { }
var rt = app.Services.GetService<Sora.Core.Hosting.Runtime.IAppRuntime>();
rt?.Discover();
rt?.Start();

// Ensure the default message bus is created so subscriptions are provisioned and consumers start
try
{
    var selector = app.Services.GetService<IMessageBusSelector>();
    _ = selector?.ResolveDefault(app.Services);
}
catch { /* ignore */ }

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();