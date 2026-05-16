using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// Boots a real Koan framework instance for integration testing.
/// Sets AppHost.Current so static facades (Model.*, Client.*, Chain.*, etc.) work.
/// </summary>
public sealed class KoanTestFixture : IDisposable
{
    public IServiceProvider Services { get; }
    public IAiAdapterRegistry AdapterRegistry { get; }

    public KoanTestFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Ai:AutoDiscoveryEnabled"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostApplicationLifetime>(new NoopHostApplicationLifetime());
        services.AddLogging();

        // Boot Koan framework -- this triggers all auto-registrars
        services.AddKoan();

        Services = services.BuildServiceProvider();

        // Initialize KoanEnv so static helpers work
        try
        {
            KoanEnv.TryInitialize(Services);
        }
        catch
        {
            // KoanEnv is sticky per-process; ignore duplicate initialization failures.
        }

        // Set global host so static facades work
        AppHost.Current = Services;

        // Get the adapter registry for registering test adapters
        AdapterRegistry = Services.GetRequiredService<IAiAdapterRegistry>();
    }

    /// <summary>Register a test adapter with specific capabilities.</summary>
    public void RegisterAdapter(string id, params string[] capabilities)
    {
        var adapter = new TestCapableAdapter(id, capabilities);
        AdapterRegistry.Add(adapter);
    }

    public void Dispose()
    {
        if (ReferenceEquals(AppHost.Current, Services))
        {
            AppHost.Current = null;
        }

        if (Services is IDisposable disposable)
            disposable.Dispose();
    }
}
