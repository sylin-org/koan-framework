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
using Microsoft.Extensions.Logging;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using Sora.Data.Mongo;
using Sora.Messaging.RabbitMq;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// Container-only sample guard
if (!Sora.Core.SoraEnv.InContainer)
{
    Console.Error.WriteLine("S8.Flow.Api is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Services.AddSora(); // core + data + health + scheduling
// Register Mongo data adapter so Data<> resolves 'mongo' provider inside containers
builder.Services.AddMongoAdapter(o =>
{
    // Prefer ConnectionStrings:Default; database comes from Sora:Data:Mongo:Database (env-provided)
    o.ConnectionString = builder.Configuration.GetConnectionString("Default") ?? o.ConnectionString;
});
// Messaging core is required so the bus selector exists and RabbitMQ consumers can start
builder.Services.AddMessagingCore();
// Ensure RabbitMQ provider is registered (factory + health + optional inbox discovery)
builder.Services.AddRabbitMq();
builder.Services.AddSoraFlow();

builder.Services.Configure<FlowOptions>(o =>
{
    // Default tags act as fallback when model has no AggregationTag attributes.
    // Our Sensor model carries [AggregationTag(Keys.Sensor.Key)], so this isn't required,
    // but we keep a conservative default for other models.
    o.AggregationTags = new[] { Keys.Sensor.Key };
    // Enable purge for VO-heavy workloads and keep keyed retention modest by default
    o.PurgeEnabled = true;
    o.KeyedTtl = TimeSpan.FromDays(14);
    // Keep canonical clean: drop VO tags from canonical/lineage
    o.CanonicalExcludeTagPrefixes = new[] { "reading.", "sensorreading." };
});

builder.Services.AddControllers();
builder.Services.AddRouting();
builder.Services.AddHostedService<S8.Flow.Api.Hosting.LatestReadingProjector>();
builder.Services.AddHostedService<S8.Flow.Api.Hosting.WindowReadingProjector>();

// Message-driven adapters: handle TelemetryEvent and persist to Flow intake
builder.Services.OnMessages(h =>
{
    // Include CancellationToken in handler signature; align with minimal TelemetryEvent envelope
    h.On<TelemetryEvent>(async (env, msg, ct) =>
    {
        var sp = Sora.Core.Hosting.App.AppHost.Current;
        var log = sp?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        log?.CreateLogger("S8.Flow.Api.Handlers")?.LogInformation(
            "TelemetryEvent received {System}/{Adapter} sensor={SensorExternalId} from {Source} at {At}",
            msg.System, msg.Adapter, msg.SensorExternalId, msg.Source, msg.CapturedAt);

        // Coerce to nullable value dictionary to satisfy StagePayload's Dictionary<string, object?> type
        Dictionary<string, object?> payload = msg
            .ToPayloadDictionary()
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase);
        // Provide the model's configured aggregation key so association can succeed
        payload[Keys.Sensor.Key] = msg.SensorExternalId;

        // Enqueue into the per-model, typed intake for Sensor so model-aware workers can process it
        var typed = new StageRecord<Sensor>
        {
            Id = Guid.NewGuid().ToString("n"),
            SourceId = msg.Source ?? msg.Adapter,
            OccurredAt = msg.CapturedAt,
            StagePayload = payload
        };
        await typed.Save(FlowSets.StageShort(FlowSets.Intake));
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

// Pre-register known message aliases so the consumer resolves to the exact handler type
try
{
    var aliases = app.Services.GetService<ITypeAliasRegistry>();
    // Ensure alias mapping for TelemetryEvent points at the same runtime type as our registered handler
    if (aliases is not null) _ = aliases.GetAlias(typeof(TelemetryEvent));
}
catch { /* ignore */ }

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();

// Ensure the latest.reading view set exists (idempotent) — custom view
try
{
    using (Sora.Data.Core.DataSetContext.With(Sora.Flow.Infrastructure.FlowSets.ViewShort(S8.Flow.Api.Hosting.LatestReadingProjector.ViewName)))
    { await Sora.Data.Core.Data<S8.Flow.Shared.SensorLatestReading, string>.FirstPage(1); }
}
catch { }

// Ensure the window.5m view set exists (idempotent) — custom view
try
{
    using (Sora.Data.Core.DataSetContext.With(Sora.Flow.Infrastructure.FlowSets.ViewShort(S8.Flow.Api.Hosting.WindowReadingProjector.ViewName)))
    { await Sora.Data.Core.Data<S8.Flow.Shared.SensorWindowReading, string>.FirstPage(1); }
}
catch { }

app.Run();