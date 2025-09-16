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
    Console.Error.WriteLine("S8.Location.Adapters.Healthcare is container-only. Use samples/S8.Compose/docker-compose.yml.");
    return;
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Koan framework with auto-configuration  
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();

[FlowAdapter(system: "healthcare", adapter: "healthcare", DefaultSource = "healthcare")]
public sealed class HealthcareLocationAdapter : BackgroundService
{
    private readonly ILogger<HealthcareLocationAdapter> _logger;

    public HealthcareLocationAdapter(ILogger<HealthcareLocationAdapter> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[HEALTHCARE] Starting location adapter");

        var sampleLocations = GetHealthcareSampleData();
        var lastAnnounce = DateTimeOffset.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Send complete healthcare locations every 5 minutes
                if (DateTimeOffset.UtcNow - lastAnnounce > TimeSpan.FromMinutes(5))
                {
                    _logger.LogInformation("[HEALTHCARE] Sending {Count} locations", sampleLocations.Count);

                    foreach (var (externalId, address) in sampleLocations)
                    {
                        var location = new Location
                        {
                            Id = externalId, // HP1, HP2, etc. - tracked via Flow metadata (source.system, source.adapter)
                            Address = address
                        };

                        _logger.LogDebug("[HEALTHCARE] Sending location {ExternalId}: {Address}", externalId, address);
                        await location.Send();
                    }

                    lastAnnounce = DateTimeOffset.UtcNow;
                    _logger.LogInformation("[HEALTHCARE] Location batch sent");
                }

                await Task.Delay(TimeSpan.FromSeconds(45), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HEALTHCARE] Error in adapter loop");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }

    private static Dictionary<string, string> GetHealthcareSampleData() => new()
    {
        ["HP1"] = "96 First Street, Middle of Nowhere, Pennsylvania",
        ["HP2"] = "1600 Pennsylvania Avenue, Washington, District of Columbia",
        ["HP3"] = "350 5th Ave, New York, New York",
        ["HP4"] = "One Microsoft Way, Redmond, Washington 98052",
        ["HP5"] = "1 Apple Park Way, Cupertino, California 95014"
    };
}