using Koan.ZenGarden;
using Koan.ZenGarden.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenFixture : IAsyncLifetime
{
    public string? Endpoint { get; } =
        Environment.GetEnvironmentVariable("KOAN_TESTS_ZENGARDEN_ENDPOINT");

    public string? GardenStoneSelector { get; } =
        Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.GardenStone);

    public string EndpointDisplay =>
        !string.IsNullOrWhiteSpace(Endpoint)
            ? Endpoint
            : !string.IsNullOrWhiteSpace(GardenStoneSelector)
                ? $"{Constants.EnvironmentVariables.GardenStone}={GardenStoneSelector}"
                : "(auto-discovery)";

    public bool RequireAvailable { get; } = ReadBooleanEnvironment("KOAN_TESTS_ZENGARDEN_REQUIRED");

    public string PreferredOffering { get; } =
        Environment.GetEnvironmentVariable("KOAN_TESTS_ZENGARDEN_OFFERING")
        ?? "mongodb";

    public IZenGardenClient Client { get; private set; } = default!;
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Client = new ZenGardenClient(
            NullLogger<ZenGardenClient>.Instance,
            new ZenGardenOptions
            {
                Endpoint = Endpoint,
                EnableDiscovery = true,
                HttpTimeoutSeconds = 8,
                StreamReconnectDelaySeconds = 1,
                DedupeWindowSize = 2048
            });

        try
        {
            await Client.Catalog(
                new ZenGardenSubscription { ToolType = ZenGardenToolType.Offering },
                CancellationToken.None);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool ReadBooleanEnvironment(string key)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" => true,
            "true" => true,
            "yes" => true,
            "y" => true,
            "on" => true,
            _ => false
        };
    }
}
