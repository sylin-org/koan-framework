using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using S8.Location.Core.Models;
using Koan.Core;
using Koan.Core.Hosting;
using Koan.Data.Core;
using Koan.Flow.Attributes;
using Koan.Messaging;

var builder = Host.CreateApplicationBuilder(args);

if (!KoanEnv.InContainer)
{
    Console.Error.WriteLine("S8.Location.Adapters.Inventory is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Koan framework with auto-configuration
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();

[FlowAdapter(system: "inventory", adapter: "inventory", DefaultSource = "inventory")]
public sealed class InventoryLocationAdapter : BackgroundService
{
    private readonly ILogger<InventoryLocationAdapter> _logger;

    public InventoryLocationAdapter(ILogger<InventoryLocationAdapter> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[INVENTORY] Starting location adapter");

        var sampleLocations = GetInventorySampleData();
        var lastAnnounce = DateTimeOffset.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Send complete inventory every 5 minutes
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
                {
                    _logger.LogInformation("[INVENTORY] Sending {Count} locations", sampleLocations.Count);

                    foreach (var (externalId, address) in sampleLocations)
                    {
                        var location = new Location
                        {
                            Id = externalId, // IS1, IS2, etc. - tracked via Flow metadata (source.system, source.adapter)
                            Address = address
                        };

                        _logger.LogDebug("[INVENTORY] Sending location {ExternalId}: {Address}", externalId, address);
                        await location.Send();
                    }

                    lastAnnounce = DateTimeOffset.UtcNow;
                    _logger.LogInformation("[INVENTORY] Location batch sent");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[INVENTORY] Error in adapter loop");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }

    private static Dictionary<string, string> GetInventorySampleData() => new()
    {
        ["IS1"] = "96 1st street Middle-of-Nowhere PA",
        ["IS2"] = "1600 Pennsylvania Ave Washington DC",
        ["IS3"] = "350 Fifth Avenue New York NY",
        ["IS4"] = "1 Microsoft Way Redmond WA 98052",
        ["IS5"] = "1 Apple Park Way Cupertino CA 95014"
    };
}