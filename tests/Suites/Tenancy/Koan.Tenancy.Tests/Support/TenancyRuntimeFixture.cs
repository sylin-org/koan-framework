using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tenancy.Tests.Support;

/// <summary>
/// A no-Docker integration host (ARCH-0079) that boots a real <c>AddKoan()</c> with the JSON adapter AND the
/// <c>Koan.Tenancy</c> module referenced — so the tenancy auto-registrar is discovered and the fail-closed
/// guard is wired. Proves tenancy through real reflective discovery, not a fake.
/// </summary>
internal sealed class TenancyRuntimeFixture : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private readonly string _rootPath;

    private TenancyRuntimeFixture(IntegrationHost host, string rootPath)
    {
        _host = host;
        _rootPath = rootPath;
    }

    public IServiceProvider Services => _host.Services;

    public static async Task<TenancyRuntimeFixture> CreateAsync(
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        string adapter = "sqlite",
        string environment = "Test",
        Action<IServiceCollection>? configureServices = null,
        bool withLocalStorage = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "Koan-Tenancy", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);

        // SQLite is the no-Docker adapter that ANNOUNCES isolation (DataCaps.Isolation.RowScoped) — tenancy needs
        // an isolating store, so the guard tests AND the AssertNoTenantLeak proof run on it. The JSON adapter does
        // NOT announce isolation; a tenant-scoped op there fails closed (a dedicated negative test boots adapter:"json").
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = environment,
            ["Koan:Data:Sources:Default:Adapter"] = adapter,
            // Pin Standalone so a Development boot (used to exercise the dev-open posture) never arms the
            // self-orchestration heuristic; the tenancy suite references no orchestration adapter, so this is inert
            // belt-and-suspenders.
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
        };
        if (string.Equals(adapter, "sqlite", StringComparison.OrdinalIgnoreCase))
            settings["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(root, "tenancy.db")}";
        else if (string.Equals(adapter, "json", StringComparison.OrdinalIgnoreCase))
            settings["Koan:Data:Json:DirectoryPath"] = root;

        if (withLocalStorage)
        {
            // STOR-0011: a no-Docker Local storage profile rooted under the per-fixture temp dir, so the storage
            // blob-key tenant-isolation proof runs through a real provider.
            var storageDir = Path.Combine(root, "storage");
            Directory.CreateDirectory(storageDir);
            settings["Koan:Storage:Providers:Local:BasePath"] = storageDir;
            settings["Koan:Storage:DefaultProfile"] = "test";
            settings["Koan:Storage:Profiles:test:Provider"] = "local";
            settings["Koan:Storage:Profiles:test:Container"] = "blobs";
        }

        if (extraSettings is not null)
        {
            foreach (var kv in extraSettings)
                settings[kv.Key] = kv.Value;
        }

        // The per-host IHostEnvironment drives the prod-boot pre-flight (ARCH-0099 §1); default "Test" is
        // non-production. A test can boot environment: "Production" to exercise the boot-refusal, and inject an
        // ITenantResolver via configureServices to satisfy it.
        var builder = KoanIntegrationHost.Configure()
            .WithEnvironment(environment)
            .WithSettings(settings)
            .ConfigureServices(s => s.AddKoan());
        if (configureServices is not null)
            builder.ConfigureServices(configureServices);

        var host = await builder.StartAsync().ConfigureAwait(false);

        AppHost.Current = host.Services;

        return new TenancyRuntimeFixture(host, root);
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
        {
            AppHost.Current = null;
        }

        TestHooks.ResetDataConfigs();

        await _host.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
