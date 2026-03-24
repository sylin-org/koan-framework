using System.Collections.Concurrent;
using System.Text.Json;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
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
var step = 1;
void StepLog(string title)
{
    Console.WriteLine($"[Step {step:00}] {title}");
    step++;
}
void DetailLog(string message)
{
    Console.WriteLine($"         {message}");
}

StepLog("Building host and loading configuration.");
var builder = Host.CreateApplicationBuilder(args);

var endpointOverride = Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_ENDPOINT")
    ?? Environment.GetEnvironmentVariable("KOAN_TESTS_ZENGARDEN_ENDPOINT");
if (!string.IsNullOrWhiteSpace(endpointOverride))
{
    builder.Configuration["Koan:ZenGarden:Endpoint"] = endpointOverride;
    DetailLog($"Using explicit endpoint override: {endpointOverride}");
}
if (string.IsNullOrWhiteSpace(builder.Configuration["Koan:Ai:AllowDiscoveryInNonDev"]))
{
    builder.Configuration["Koan:Ai:AllowDiscoveryInNonDev"] = "true";
    DetailLog("Enabled AI discovery for non-development environment.");
}

// Reference-driven bootstrap: adapters self-register when their packages are referenced.
builder.Services.AddKoan();
DetailLog("Registered Koan services via AddKoan().");

StepLog("Starting host and activating auto-registrars.");
using var host = builder.Build();
await host.StartAsync();
DetailLog("Host started.");

AppHost.Current ??= host.Services;
DetailLog("AppHost.Current assigned.");

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

StepLog("Subscribing to offering/storage availability streams.");
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
DetailLog("Subscribed: offering:mongodb, offering:ollama, seed-bank:default.");

var wishedCapabilities = ParseCapabilities(
    Environment.GetEnvironmentVariable("KOAN_OLLAMA_WISH_CAPS"),
    "llama3.2,nomic-embed-text");
var ollamaWaitSeconds = ParseInt(Environment.GetEnvironmentVariable("KOAN_OLLAMA_WAIT_SECONDS"), 90);
var ollamaRefreshSeconds = ParseInt(Environment.GetEnvironmentVariable("KOAN_OLLAMA_REFRESH_SECONDS"), 2);

StepLog("Submitting a wishful capability request for Ollama.");
DetailLog($"Requested capabilities: {string.Join(", ", wishedCapabilities)}");
ZenGardenCapabilityWish? capabilityWish = null;
IDisposable? capabilityWishSubscription = null;
try
{
    capabilityWish = await ZenGarden.Capability.Wish("ollama", wishedCapabilities);
    capabilityWishSubscription = ZenGarden.On<ZenGardenCapabilityProgressEvent>(
        capabilityWish,
        (evt, ct) => RecordCapabilityEvent(capabilityEvents, evt));

    Console.WriteLine($"[Wish] request={capabilityWish.RequestId} status={capabilityWish.Status} missing={string.Join(",", capabilityWish.Missing)}");
    DetailLog("Capability progress subscription attached.");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"[Wish] capability request failed: {ex.Message}");
    Console.WriteLine();
}

try
{
    StepLog("Reading live Zen Garden catalogs.");
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

string? orchestratorUri = null;
string? chatModel = null;
string? embedModel = null;

try
{
    StepLog("Checking for Ollama orchestrator in catalog.");
    var allOfferings = await ZenGarden.Offering.Catalog();
    var orchestrator = allOfferings.FirstOrDefault(t =>
        string.Equals(t.ToolFqid, "ollama:orchestrator", StringComparison.OrdinalIgnoreCase) && t.Ready);

    if (orchestrator?.Connection?.Uris.Count > 0)
    {
        orchestratorUri = orchestrator.Connection.Uris[0].TrimEnd('/');
        Console.WriteLine($"[Orchestrator] Detected at {orchestratorUri} (stone={orchestrator.StoneName})");
    }
    else
    {
        Console.WriteLine("[Orchestrator] No ready ollama:orchestrator in catalog; skipping orchestrator steps.");
    }

    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"[Orchestrator] Detection failed: {ex.Message}");
    Console.WriteLine();
}

if (orchestratorUri is not null)
{
    using var orchestratorHttp = new HttpClient
    {
        BaseAddress = new Uri(orchestratorUri),
        Timeout = TimeSpan.FromSeconds(60)
    };
    var snakeJson = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    try
    {
        StepLog("Fetching model recommendations from orchestrator.");
        var capabilities = new[] { "completion", "embedding" };

        foreach (var capability in capabilities)
        {
            var body = await orchestratorHttp.GetString($"/v1/recommendations?capability={capability}");
            var recs = JsonSerializer.Deserialize<OrchestratorRecommendations>(body, snakeJson);

            if (recs?.Recommendations is { Count: > 0 })
            {
                Console.WriteLine($"[Recommendations] {capability} ({recs.Recommendations.Count} models):");
                foreach (var rec in recs.Recommendations.Take(3))
                {
                    Console.WriteLine($"  #{rec.Rank} {rec.Model}  score={rec.Score} verdict={rec.Verdict} params={rec.ParameterSize} ctx={rec.ContextLength}");
                    if (rec.Reasoning is { Count: > 0 })
                    {
                        Console.WriteLine($"       {string.Join("; ", rec.Reasoning)}");
                    }
                }

                var topModel = recs.Recommendations[0].Model;
                if (capability == "completion") chatModel = topModel;
                else if (capability == "embedding") embedModel = topModel;
            }
            else
            {
                Console.WriteLine($"[Recommendations] {capability}: no models recommended.");
            }
        }

        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Recommendations] Failed: {ex.Message}");
        Console.WriteLine();
    }

    try
    {
        StepLog("Exercising recommended models via orchestrator proxy.");

        if (chatModel is not null)
        {
            Console.WriteLine($"[Orchestrator] Chat probe: model={chatModel}");
            var chatPayload = JsonSerializer.Serialize(new
            {
                model = chatModel,
                messages = new[] { new { role = "user", content = "Respond with one word: koan" } },
                stream = false
            });

            var chatResponse = await orchestratorHttp.Post("/api/chat",
                new StringContent(chatPayload, System.Text.Encoding.UTF8, "application/json"));
            var chatBody = await chatResponse.Content.ReadAsStringAsync();

            if (chatResponse.IsSuccessStatusCode)
            {
                using var chatDoc = JsonDocument.Parse(chatBody);
                var content = chatDoc.RootElement.GetProperty("message").GetProperty("content").GetString();
                Console.WriteLine($"[Orchestrator] Chat response: {content}");
            }
            else
            {
                Console.WriteLine($"[Orchestrator] Chat failed ({(int)chatResponse.StatusCode}): {chatBody}");
            }
        }

        if (embedModel is not null)
        {
            Console.WriteLine($"[Orchestrator] Embed probe: model={embedModel}");
            var embedPayload = JsonSerializer.Serialize(new
            {
                model = embedModel,
                input = "The zen garden is quiet"
            });

            var embedResponse = await orchestratorHttp.Post("/api/embed",
                new StringContent(embedPayload, System.Text.Encoding.UTF8, "application/json"));
            var embedBody = await embedResponse.Content.ReadAsStringAsync();

            if (embedResponse.IsSuccessStatusCode)
            {
                using var embedDoc = JsonDocument.Parse(embedBody);
                var embeddings = embedDoc.RootElement.GetProperty("embeddings");
                if (embeddings.GetArrayLength() > 0)
                {
                    var first = embeddings[0];
                    var dims = first.GetArrayLength();
                    var preview = string.Join(", ", first.EnumerateArray().Take(5).Select(v => v.GetDouble().ToString("F4")));
                    Console.WriteLine($"[Orchestrator] Embedding: {dims} dimensions [{preview}, ...]");
                }
            }
            else
            {
                Console.WriteLine($"[Orchestrator] Embed failed ({(int)embedResponse.StatusCode}): {embedBody}");
            }
        }

        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Orchestrator] Exercise failed: {ex.Message}");
        Console.WriteLine();
    }
}

StepLog("Resolving auto-initialization intents for MongoDB and Ollama.");
using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var resolver = services.GetRequiredService<IZenGardenInitializationProvider>();
    var mongoOptions = services.GetRequiredService<IOptionsSnapshot<MongoOptions>>();
    var aiSources = services.GetRequiredService<IAiSourceRegistry>();

    Console.WriteLine("[Diagnostics] Resolving connection intents...");
    var mongoResolved = await resolver.Resolve(ZenGardenConnectionIntent.ForOffering("mongodb"));
    var ollamaResolved = await resolver.Resolve(ZenGardenConnectionIntent.ForOffering("ollama"));
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
        engineAvailable = Client.IsAvailable
    };

    Console.WriteLine(JsonSerializer.Serialize(diagnostic, json));
    Console.WriteLine();
}

StepLog("Running MongoDB probe (write + read).");
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
StepLog("Waiting for Ollama source readiness (post-initialization loop).");
var ollamaReady = await WaitForOllamaChatReadiness(
    host.Services,
    capabilityEvents,
    wishedCapabilities,
    ollamaWaitSeconds,
    ollamaRefreshSeconds,
    CancellationToken.None);
if (!ollamaReady)
{
    Console.WriteLine("[Ollama] Chat source is still not ready; running probe anyway.");
    Console.WriteLine();
}

StepLog("Running Ollama probe (Client.Chat).");
try
{
    Console.WriteLine("[Ollama] Running chat probe...");
    var response = await Client.Chat("Respond with one word: koan");
    Console.WriteLine($"[Ollama] response: {response}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Ollama] probe failed: {ex.Message}");
}

var watchSeconds = ParseInt(Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_WATCH_SECONDS"), 5);
if (watchSeconds > 0)
{
    StepLog("Watching live stream updates.");
    Console.WriteLine();
    Console.WriteLine($"[Events] Watching subscriptions for {watchSeconds}s...");
    await Task.Delay(TimeSpan.FromSeconds(watchSeconds));
}

StepLog("Printing captured event summaries.");
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

StepLog("Stopping host and exiting sample.");
capabilityWishSubscription?.Dispose();
await host.StopAsync();
return;

static ValueTask RecordEvent(
    ConcurrentQueue<ZenGardenEventEntry> target,
    string channel,
    ZenGardenAvailabilityEvent evt)
{
    var entry = new ZenGardenEventEntry(
        DateTimeOffset.UtcNow,
        channel,
        evt.Kind.ToString(),
        evt.Current.ToolFqid,
        evt.Current.Ready,
        evt.Current.Revision);
    target.Enqueue(entry);

    Console.WriteLine($"[Event][{entry.TimestampUtc:HH:mm:ss}] {entry.Channel} kind={entry.Kind} tool={entry.ToolFqid} ready={entry.Ready} rev={entry.Revision}");

    while (target.Count > 200 && target.TryDequeue(out _))
    {
    }

    return ValueTask.CompletedTask;
}

static ValueTask RecordCapabilityEvent(
    ConcurrentQueue<ZenGardenCapabilityEventEntry> target,
    ZenGardenCapabilityProgressEvent evt)
{
    var entry = new ZenGardenCapabilityEventEntry(
        DateTimeOffset.UtcNow,
        evt.Kind.ToString(),
        evt.Wish.RequestId,
        evt.Wish.Missing.ToArray());
    target.Enqueue(entry);

    Console.WriteLine($"[Capability][{entry.TimestampUtc:HH:mm:ss}] kind={entry.Kind} request={entry.RequestId} missing={string.Join(",", entry.Missing)}");

    while (target.Count > 200 && target.TryDequeue(out _))
    {
    }

    return ValueTask.CompletedTask;
}

static async Task<bool> WaitForOllamaChatReadiness(
    IServiceProvider rootServices,
    ConcurrentQueue<ZenGardenCapabilityEventEntry> capabilityEvents,
    IReadOnlyList<string> wishedCapabilities,
    int timeoutSeconds,
    int refreshSeconds,
    CancellationToken cancellationToken)
{
    if (timeoutSeconds <= 0)
    {
        Console.WriteLine("[Ollama][Wait] skipped (timeout <= 0).");
        return false;
    }

    if (refreshSeconds <= 0)
    {
        refreshSeconds = 1;
    }

    Console.WriteLine($"[Ollama][Wait] timeout={timeoutSeconds}s refresh={refreshSeconds}s wished={string.Join(",", wishedCapabilities)}");
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
    var token = linked.Token;

    var attempt = 0;
    while (!token.IsCancellationRequested)
    {
        attempt++;

        if (TryGetOllamaChatStatus(rootServices, out var sourceCount, out var memberCount))
        {
            Console.WriteLine($"[Ollama][Wait] ready after attempt={attempt} sources={sourceCount} members={memberCount}");
            return true;
        }

        var lastProgress = capabilityEvents.LastOrDefault();
        var progress = lastProgress is null
            ? "none"
            : $"{lastProgress.Kind} missing={string.Join(",", lastProgress.Missing)}";
        Console.WriteLine($"[Ollama][Wait] attempt={attempt} source-not-ready progress={progress}");

        var refreshed = await RefreshOllamaContributor(rootServices, token).ConfigureAwait(false);
        if (!refreshed)
        {
            Console.WriteLine("[Ollama][Wait] no Ollama contributor found to refresh.");
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(refreshSeconds), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            break;
        }
    }

    var finalReady = TryGetOllamaChatStatus(rootServices, out var finalSources, out var finalMembers);
    Console.WriteLine($"[Ollama][Wait] timed out. ready={finalReady} sources={finalSources} members={finalMembers}");
    return finalReady;
}

static bool TryGetOllamaChatStatus(
    IServiceProvider rootServices,
    out int sourceCount,
    out int memberCount)
{
    var sourceRegistry = rootServices.GetRequiredService<IAiSourceRegistry>();
    var sources = sourceRegistry
        .GetSourcesWithCapability("Chat")
        .Where(source => string.Equals(source.Provider, "ollama", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    sourceCount = sources.Length;
    memberCount = sources.Sum(source => source.Members.Count);
    return memberCount > 0;
}

static async Task<bool> RefreshOllamaContributor(
    IServiceProvider rootServices,
    CancellationToken cancellationToken)
{
    using var scope = rootServices.CreateScope();
    var contributors = scope.ServiceProvider.GetServices<IAiAdapterContributor>();
    foreach (var contributor in contributors)
    {
        var contributorName = contributor.GetType().FullName ?? contributor.GetType().Name;
        if (!contributorName.Contains("OllamaAdapterContributor", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        await contributor.Contribute(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        return true;
    }

    return false;
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

sealed record OrchestratorRecommendations(
    string Capability,
    List<ModelRecommendation> Recommendations);

sealed record ModelRecommendation(
    string Model,
    int Rank,
    int Score,
    string Verdict,
    string? ParameterSize,
    string? QuantizationLevel,
    int? ContextLength,
    List<string>? Reasoning);
