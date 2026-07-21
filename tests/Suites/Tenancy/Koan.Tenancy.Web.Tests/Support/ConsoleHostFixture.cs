using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tenancy.Web.Tests.Support;

/// <summary>
/// A no-Docker integration host (ARCH-0079) for the tenancy operator console: a real <c>AddKoan()</c> boot with the
/// SQLite adapter and <c>Koan.Tenancy.Web</c> referenced, so the console's module, entities, and job type are
/// discovered through real reflective composition. Always boots the <b>"Test"</b> environment — the Koan.Web MVC
/// service graph cannot pass a generic host's Development-env <c>ValidateOnBuild</c> (it resolves
/// <c>IWebHostEnvironment</c>, absent outside a web host), and the console needs no dev-open boot to be proven.
/// </summary>
internal sealed class ConsoleHostFixture : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private readonly string _rootPath;

    private ConsoleHostFixture(IntegrationHost host, string rootPath)
    {
        _host = host;
        _rootPath = rootPath;
    }

    public IServiceProvider Services => _host.Services;

    public static async Task<ConsoleHostFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Koan-Tenancy-Web", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(root, "console.db")}",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
        };

        var host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan())
            .StartAsync()
            .ConfigureAwait(false);

        AppHost.Current = host.Services;
        return new ConsoleHostFixture(host, root);
    }

    /// <summary>Bind the host and reset the per-type data-config caches so each fixture boot is clean.</summary>
    public void ResetEntityCaches()
    {
        AppHost.Current = _host.Services;
        TestHooks.ResetDataConfigs();
    }

    public async ValueTask DisposeAsync()
    {
        if (ReferenceEquals(AppHost.Current, _host.Services))
            AppHost.Current = null;

        TestHooks.ResetDataConfigs();
        await _host.DisposeAsync().ConfigureAwait(false);

        try { if (Directory.Exists(_rootPath)) Directory.Delete(_rootPath, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}
