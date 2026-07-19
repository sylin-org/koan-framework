# Sylin.Koan.ZenGarden

Automatic Zen Garden offering, storage, and capability discovery for a running Koan host.

```xml
<PackageReference Include="Sylin.Koan.ZenGarden" Version="1.0.0" />
```

```bash
dotnet add package Sylin.Koan.ZenGarden
```

Reference is intent. Keep the application's existing `AddKoan()` call; the package automatically registers the
Garden client, discovery contribution, capability resolver, and optional AI model advisor.

## First useful result

Long-lived Garden reactions use the standard hosted-service lifecycle so subscriptions start after the host and are
disposed with it:

```csharp
using Koan.ZenGarden;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
builder.Services.AddHostedService<GardenReactions>();

var app = builder.Build();
await app.RunAsync();

sealed class GardenReactions : BackgroundService
{
    private IDisposable? _mongodb;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _mongodb = ZenGarden.Offering.On(
            "mongodb",
            (change, ct) =>
            {
                Console.WriteLine($"{change.Current.ToolFqid}: {change.Kind}");
                return ValueTask.CompletedTask;
            });

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    public override void Dispose()
    {
        _mongodb?.Dispose();
        base.Dispose();
    }
}
```

The three application surfaces are deliberately small:

- `ZenGarden.Offering` watches or catalogs service offerings.
- `ZenGarden.Storage` watches or catalogs seed banks.
- `ZenGarden.Capability` requests capabilities wishfully and watches progress.

```csharp
var ollama = await ZenGarden.Offering.Catalog("ollama", cancellationToken);
var storage = await ZenGarden.Storage.Catalog(cancellationToken);

var wish = await ZenGarden.Capability.Wish(
    "ollama",
    ["llama3.2", "nomic-embed-text"],
    cancellationToken: cancellationToken);

using var progress = ZenGarden.Capability.On(
    wish,
    (change, ct) =>
    {
        Console.WriteLine($"{change.Kind}: {string.Join(", ", change.Wish.Missing)}");
        return ValueTask.CompletedTask;
    });
```

Capability wishes are non-blocking. Applications decide when a feature is useful from availability/progress events;
host startup does not wait for external orchestration.

## Configuration

Defaults work with local Moss/Koi discovery. Override only decisions the application owns:

```json
{
  "Koan": {
    "ZenGarden": {
      "Endpoint": "http://stone-01:7185",
      "EnableDiscovery": true,
      "HttpTimeoutSeconds": 30,
      "KoiDiscoveryEnabled": true,
      "PersistDiscoveryCache": true
    }
  }
}
```

Invalid ports, counts, timeouts, TTLs, or Koi durations fail options validation with the setting name. An explicit
connection intent that cannot resolve remains a corrective adapter startup failure; an absent automatic Garden is
normal inactivity.

## Contracts and boundaries

Connector authors that only need portable intent/resolution types reference
`Sylin.Koan.ZenGarden.Contracts`, not this runtime package. Mongo, Weaviate, Ollama, and S3 use that inert boundary
without acquiring Moss/Koi discovery.

This package does not provide a storage changelog client, circuit breaker, endpoint manager, manual client factory,
generic event dispatcher, or deployment topology. Standard hosted services own subscription lifetime; standard DI
owns the runtime client; storage and connector packages own their transport-specific resilience.

See [TECHNICAL.md](TECHNICAL.md) for discovery order, protocols, lifecycle, configuration, and failure semantics.
