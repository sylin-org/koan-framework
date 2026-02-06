using Koan.ZenGarden;
using Koan.ZenGarden.Models;
using Microsoft.Extensions.Logging;

Console.WriteLine("==============================================================");
Console.WriteLine("Koan.ZenGarden Tools-Domain Smoke Test");
Console.WriteLine("==============================================================");
Console.WriteLine();

var endpointOverride = Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_ENDPOINT")
    ?? Environment.GetEnvironmentVariable("KOAN_TESTS_ZENGARDEN_ENDPOINT");
var gardenStoneSelector = Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.GardenStone);
var endpointDisplay = !string.IsNullOrWhiteSpace(endpointOverride)
    ? endpointOverride
    : !string.IsNullOrWhiteSpace(gardenStoneSelector)
        ? $"{Constants.EnvironmentVariables.GardenStone}={gardenStoneSelector}"
        : "(auto-discovery)";
var preferredOffering = Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_OFFERING") ?? "mongodb";
var preferredSeedBank = Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_STORAGE") ?? "default";
var capabilityList = ParseCsv(Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_CAPABILITIES"));
var watchSeconds = ParseInt(Environment.GetEnvironmentVariable("KOAN_ZENGARDEN_WATCH_SECONDS"), 10);

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "HH:mm:ss ";
        options.SingleLine = true;
    });
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<ZenGardenClient>();
using var client = new ZenGardenClient(logger, new ZenGardenOptions
{
    Endpoint = endpointOverride,
    EnableDiscovery = true,
    HttpTimeoutSeconds = 10,
    StreamReconnectDelaySeconds = 2,
    DedupeWindowSize = 2048
});

ZenGarden.Configure(client);

Console.WriteLine($"Endpoint selector: {endpointDisplay}");
Console.WriteLine();

IReadOnlyList<ZenGardenToolSnapshot> offerings;
IReadOnlyList<ZenGardenToolSnapshot> storage;
try
{
    offerings = await ZenGarden.Offering.Catalog();
    storage = await ZenGarden.Storage.Catalog();
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to resolve/reach Zen Garden endpoint '{endpointDisplay}': {ex.Message}");
    Console.WriteLine("Set KOAN_ZENGARDEN_ENDPOINT or GARDEN_STONE, or make sure UDP discovery (port 7184) can reach a Moss node.");
    return 1;
}

Console.WriteLine($"Offerings: {offerings.Count}");
foreach (var tool in offerings.Take(10))
{
    Console.WriteLine($"  - {tool.ToolFqid} ready={tool.Ready} state={tool.State} rev={tool.Revision}");
}

Console.WriteLine($"Seed banks: {storage.Count}");
foreach (var tool in storage.Take(10))
{
    Console.WriteLine($"  - {tool.ToolFqid} ready={tool.Ready} state={tool.State} rev={tool.Revision}");
}

if (offerings.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("No offerings found. Verify the test garden has active tools.");
    return 1;
}

var selectedOffering = SelectTool(offerings, "offering:", preferredOffering);
var selectedSeedBank = SelectTool(storage, "seed-bank:", preferredSeedBank);

if (selectedOffering is null)
{
    Console.WriteLine("Could not select an offering to watch.");
    return 1;
}

if (selectedSeedBank is null && storage.Count > 0)
{
    selectedSeedBank = storage[0];
}

capabilityList = capabilityList.Count > 0
    ? capabilityList
    : DeriveRequirements(selectedOffering);

Console.WriteLine();
Console.WriteLine("Watch configuration:");
Console.WriteLine($"  offering: {selectedOffering.ToolFqid}");
Console.WriteLine($"  storage:  {(selectedSeedBank is null ? "(none)" : selectedSeedBank.ToolFqid)}");
Console.WriteLine($"  requires: {(capabilityList.Count == 0 ? "(none)" : string.Join(",", capabilityList))}");
Console.WriteLine($"  duration: {watchSeconds}s");
Console.WriteLine();

var offeringInitial = new TaskCompletionSource<ZenGardenAvailabilityEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
var storageInitial = new TaskCompletionSource<ZenGardenAvailabilityEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

using var offeringSub = capabilityList.Count == 0
    ? ZenGarden.Offering.On(
        StripPrefix(selectedOffering.ToolFqid, "offering:"),
        (evt, ct) => OnEvent("offering", evt, offeringInitial))
    : ZenGarden.Offering.On(
        StripPrefix(selectedOffering.ToolFqid, "offering:"),
        capabilityList,
        (evt, ct) => OnEvent("offering", evt, offeringInitial));

using var storageSub = selectedSeedBank is null
    ? null
    : ZenGarden.Storage.On(
        StripPrefix(selectedSeedBank.ToolFqid, "seed-bank:"),
        (evt, ct) => OnEvent("storage", evt, storageInitial));

using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
await WaitForInitialEventAsync(offeringInitial.Task, "offering", warmupCts.Token);
if (selectedSeedBank is not null)
{
    await WaitForInitialEventAsync(storageInitial.Task, "storage", warmupCts.Token);
}

Console.WriteLine();
Console.WriteLine("Streaming live events...");
Console.WriteLine();

if (watchSeconds > 0)
{
    await Task.Delay(TimeSpan.FromSeconds(watchSeconds));
}

Console.WriteLine();
Console.WriteLine("Done.");
return 0;

static ValueTask OnEvent(
    string channel,
    ZenGardenAvailabilityEvent evt,
    TaskCompletionSource<ZenGardenAvailabilityEvent> initial)
{
    Console.WriteLine($"{DateTimeOffset.UtcNow:HH:mm:ss} [{channel}] kind={evt.Kind} fqid={evt.Current.ToolFqid} ready={evt.Current.Ready} rev={evt.Current.Revision}");
    initial.TrySetResult(evt);
    return ValueTask.CompletedTask;
}

static async Task WaitForInitialEventAsync(
    Task<ZenGardenAvailabilityEvent> eventTask,
    string channel,
    CancellationToken cancellationToken)
{
    try
    {
        await eventTask.WaitAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"Timed out waiting for initial {channel} event.");
    }
}

static ZenGardenToolSnapshot? SelectTool(
    IReadOnlyList<ZenGardenToolSnapshot> tools,
    string prefix,
    string preferredName)
{
    if (tools.Count == 0)
    {
        return null;
    }

    var preferredFqid = $"{prefix}{preferredName}".ToLowerInvariant();
    var preferred = tools.FirstOrDefault(t =>
        string.Equals(t.ToolFqid, preferredFqid, StringComparison.OrdinalIgnoreCase));
    return preferred ?? tools[0];
}

static IReadOnlyList<string> DeriveRequirements(ZenGardenToolSnapshot tool)
{
    var derived = new List<string>();
    foreach (var cap in tool.Capabilities)
    {
        if (cap.Value.Count == 0)
        {
            continue;
        }

        derived.Add($"{cap.Key}:{cap.Value[0]}");
        if (derived.Count >= 2)
        {
            break;
        }
    }

    return derived;
}

static IReadOnlyList<string> ParseCsv(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return Array.Empty<string>();
    }

    return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .ToArray();
}

static int ParseInt(string? raw, int fallback)
{
    return int.TryParse(raw, out var parsed) ? parsed : fallback;
}

static string StripPrefix(string fqid, string prefix)
{
    if (fqid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return fqid[prefix.Length..];
    }

    return fqid;
}
