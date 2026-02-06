using System.Collections.Concurrent;
using System.Text.Json;
using Koan.AI;
using Koan.AI.Contracts.Sources;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Connector.Mongo;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.ZenGarden;
using Koan.ZenGarden.Core;
using Koan.ZenGarden.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var json = new JsonSerializerOptions { WriteIndented = true };

var builder = Host.CreateApplicationBuilder(args);

var endpointOverride = Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_ENDPOINT")
    ?? Environment.GetEnvironmentVariable("KOAN_TESTS_ZENGARDEN_ENDPOINT");
if (!string.IsNullOrWhiteSpace(endpointOverride))
{
    builder.Configuration["Koan:ZenGarden:Endpoint"] = endpointOverride;
}
if (string.IsNullOrWhiteSpace(builder.Configuration["Koan:Ai:AllowDiscoveryInNonDev"]))
{
    builder.Configuration["Koan:Ai:AllowDiscoveryInNonDev"] = "true";
}

// Reference-driven bootstrap: adapters self-register when their packages are referenced.
builder.Services.AddKoan();

using var host = builder.Build();
await host.StartAsync();

AppHost.Current ??= host.Services;

var endpointDisplay = !string.IsNullOrWhiteSpace(endpointOverride)
    ? endpointOverride
    : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.GardenStone))
        ? $"{Constants.EnvironmentVariables.GardenStone}={Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.GardenStone)}"
        : "(auto-discovery)";

Console.WriteLine("==============================================================");
Console.WriteLine("S17.ZenGardenTest (Console)");
Console.WriteLine("MongoDB + Ollama auto-resolution via adapter references");
Console.WriteLine("==============================================================");
Console.WriteLine($"Endpoint selector: {endpointDisplay}");
Console.WriteLine();

var events = new ConcurrentQueue<ZenGardenEventEntry>();
var capabilityEvents = new ConcurrentQueue<ZenGardenCapabilityEventEntry>();
using var mongoSubscription = ZenGarden.On<ZenGardenAvailabilityEvent>(
    ZenGardenSubscription.ForOffering("mongodb"),
    (evt, ct) => RecordEvent(events, "offering:mongodb", evt));

using var ollamaSubscription = ZenGarden.On<ZenGardenAvailabilityEvent>(
    ZenGardenSubscription.ForOffering("ollama"),
    (evt, ct) => RecordEvent(events, "offering:ollama", evt));

using var storageSubscription = ZenGarden.Storage.On(
    "default",
    (evt, ct) => RecordEvent(events, "seed-bank:default", evt));

var wishedCapabilities = ParseCapabilities(
    Environment.GetEnvironmentVariable("KOAN_OLLAMA_WISH_CAPS"),
    "llama3.2,nomic-embed-text");

ZenGardenCapabilityWish? capabilityWish = null;
IDisposable? capabilityWishSubscription = null;
try
{
    capabilityWish = await ZenGarden.Capability.Wish("ollama", wishedCapabilities);
    capabilityWishSubscription = ZenGarden.On<ZenGardenCapabilityProgressEvent>(
        capabilityWish,
        (evt, ct) => RecordCapabilityEvent(capabilityEvents, evt));

    Console.WriteLine($"[Wish] request={capabilityWish.RequestId} status={capabilityWish.Status} missing={string.Join(",", capabilityWish.Missing)}");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"[Wish] capability request failed: {ex.Message}");
    Console.WriteLine();
}

try
{
    Console.WriteLine("[Catalog] Loading offerings + seed-banks...");
    var offerings = await ZenGarden.Offering.Catalog();
    var storage = await ZenGarden.Storage.Catalog();

    Console.WriteLine($"[Catalog] Offerings: {offerings.Count}");
    foreach (var item in offerings.Take(10))
    {
        Console.WriteLine($"  - {item.ToolFqid} ready={item.Ready} state={item.State} rev={item.Revision}");
    }

    Console.WriteLine($"[Catalog] Seed banks: {storage.Count}");
    foreach (var item in storage.Take(10))
    {
        Console.WriteLine($"  - {item.ToolFqid} ready={item.Ready} state={item.State} rev={item.Revision}");
    }

    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"[Catalog] Failed to load catalog: {ex.Message}");
}

using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var resolver = services.GetRequiredService<IZenGardenInitializationProvider>();
    var mongoOptions = services.GetRequiredService<IOptionsSnapshot<MongoOptions>>();
    var aiSources = services.GetRequiredService<IAiSourceRegistry>();

    Console.WriteLine("[Diagnostics] Resolving connection intents...");
    var mongoResolved = await resolver.ResolveAsync(ZenGardenConnectionIntent.ForOffering("mongodb"));
    var ollamaResolved = await resolver.ResolveAsync(ZenGardenConnectionIntent.ForOffering("ollama"));
    var ollamaSource = aiSources.GetSource("ollama");

    var diagnostic = new
    {
        mongo = new
        {
            intent = "zen-garden://mongodb",
            resolved = mongoResolved is not null,
            tool = mongoResolved?.ToolFqid,
            effectiveConnectionString = RedactConnectionString(mongoOptions.Value.ConnectionString)
        },
        ollama = new
        {
            intent = "zen-garden://ollama",
            resolved = ollamaResolved is not null,
            tool = ollamaResolved?.ToolFqid,
            sourceRegistered = ollamaSource is not null,
            sourceMembers = ollamaSource?.Members.Count ?? 0
        },
        engineAvailable = Engine.IsAvailable
    };

    Console.WriteLine(JsonSerializer.Serialize(diagnostic, json));
    Console.WriteLine();
}

try
{
    Console.WriteLine("[Mongo] Running write/read probe...");
    var probe = await new MongoNote
    {
        Text = $"probe-{Guid.NewGuid():N}",
        CreatedAtUtc = DateTimeOffset.UtcNow
    }.Save();

    var loaded = await MongoNote.Get(probe.Id);
    Console.WriteLine($"[Mongo] wrote={probe.Id} readBack={(loaded is not null)} text={loaded?.Text}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Mongo] probe failed: {ex.Message}");
}

Console.WriteLine();
try
{
    Console.WriteLine("[Ollama] Running chat probe...");
    var response = await Engine.Chat("Respond with one word: koan");
    Console.WriteLine($"[Ollama] response: {response}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Ollama] probe failed: {ex.Message}");
}

var watchSeconds = ParseInt(Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_WATCH_SECONDS"), 5);
if (watchSeconds > 0)
{
    Console.WriteLine();
    Console.WriteLine($"[Events] Watching subscriptions for {watchSeconds}s...");
    await Task.Delay(TimeSpan.FromSeconds(watchSeconds));
}

Console.WriteLine();
Console.WriteLine("[Events] Recent entries:");
foreach (var evt in events.Take(20))
{
    Console.WriteLine($"  - {evt.TimestampUtc:HH:mm:ss} {evt.Channel} {evt.Kind} {evt.ToolFqid} ready={evt.Ready} rev={evt.Revision}");
}

Console.WriteLine();
Console.WriteLine("[Capability] Recent wish updates:");
foreach (var evt in capabilityEvents.Take(20))
{
    Console.WriteLine($"  - {evt.TimestampUtc:HH:mm:ss} {evt.Kind} request={evt.RequestId} missing={string.Join(",", evt.Missing)}");
}

Console.WriteLine();
Console.WriteLine("Done.");

capabilityWishSubscription?.Dispose();
await host.StopAsync();
return;

static ValueTask RecordEvent(
    ConcurrentQueue<ZenGardenEventEntry> target,
    string channel,
    ZenGardenAvailabilityEvent evt)
{
    target.Enqueue(new ZenGardenEventEntry(
        DateTimeOffset.UtcNow,
        channel,
        evt.Kind.ToString(),
        evt.Current.ToolFqid,
        evt.Current.Ready,
        evt.Current.Revision));

    while (target.Count > 200 && target.TryDequeue(out _))
    {
    }

    return ValueTask.CompletedTask;
}

static ValueTask RecordCapabilityEvent(
    ConcurrentQueue<ZenGardenCapabilityEventEntry> target,
    ZenGardenCapabilityProgressEvent evt)
{
    target.Enqueue(new ZenGardenCapabilityEventEntry(
        DateTimeOffset.UtcNow,
        evt.Kind.ToString(),
        evt.Wish.RequestId,
        evt.Wish.Missing.ToArray()));

    while (target.Count > 200 && target.TryDequeue(out _))
    {
    }

    return ValueTask.CompletedTask;
}

static string RedactConnectionString(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "(empty)";
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
    {
        return value;
    }

    if (string.IsNullOrWhiteSpace(uri.UserInfo))
    {
        return value;
    }

    var builder = new UriBuilder(uri)
    {
        UserName = "***",
        Password = "***"
    };

    return builder.Uri.OriginalString;
}

static int ParseInt(string? raw, int fallback)
{
    return int.TryParse(raw, out var parsed) ? parsed : fallback;
}

static string[] ParseCapabilities(string? raw, string fallback)
{
    var source = string.IsNullOrWhiteSpace(raw) ? fallback : raw;
    return source
        .Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value.Trim().ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

sealed record ZenGardenEventEntry(
    DateTimeOffset TimestampUtc,
    string Channel,
    string Kind,
    string ToolFqid,
    bool Ready,
    long Revision);

sealed record ZenGardenCapabilityEventEntry(
    DateTimeOffset TimestampUtc,
    string Kind,
    string RequestId,
    string[] Missing);

sealed class MongoNote : Entity<MongoNote>
{
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
