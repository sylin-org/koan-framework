using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Koan.Data.Core; // StartKoan

namespace S16.PantryPal.Tests;

// Shared test host fixture to initialize Koan runtime once for all tests.
public sealed class TestHostFixture : IAsyncLifetime
{
    private static int _initialized;
    public IServiceProvider Services { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            return; // already initialized

        var cfgBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?>
            {
                // Minimal config overrides if needed
                ["Koan:Data:Provider"] = "Memory", // Force in-memory provider for tests
            });
        var configuration = cfgBuilder.Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    // StartKoan performs AddKoan + provider build + runtime start + sets AppHost.Current
    Services = services.StartKoan();

        // Async hooks if any (none required now)
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask; // keep host for test run duration
    }

}

[CollectionDefinition("KoanHost")] public sealed class KoanHostCollection : ICollectionFixture<TestHostFixture> { }
