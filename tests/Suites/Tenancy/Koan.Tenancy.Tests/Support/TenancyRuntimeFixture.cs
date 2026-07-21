using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Koan.Tests.Shared;
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
        // an isolating store, so the guard tests AND the AssertNoTenantLeak proof run on it. The non-isolating
        // counter-example is the deliberately non-conformant NonIsolatingFakeAdapter (adapter:"fake-noniso"); since
        // ARCH-0103 made every real KV adapter (JSON included) isolate, the fail-closed safety-net tests use the fake.
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = environment,
            // Some cells deliberately boot a Production host before later per-host fixtures. Relational DDL still
            // reads KoanEnv's process snapshot, so permit schema creation inside this disposable test database.
            ["Koan:AllowMagicInProduction"] = "true",
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
        // "fake-noniso" needs no extra settings — the source Adapter key (above) selects the registered fake factory.

        // The test assembly references Storage + Local for the storage-isolation facts. Their current package-level
        // activation validates and compiles routing for every composed host, so every fixture receives one isolated
        // local profile. Layered Storage activation itself remains owned by PMC-033.
        var storageDir = Path.Combine(root, "storage");
        settings["Koan:Storage:Providers:Local:BasePath"] = storageDir;
        settings["Koan:Storage:DefaultProfile"] = "test";
        settings["Koan:Storage:Profiles:test:Provider"] = "local";
        settings["Koan:Storage:Profiles:test:Container"] = "blobs";

        if (withLocalStorage)
        {
            // STOR-0011: a no-Docker Local storage profile rooted under the per-fixture temp dir, so the storage
            // blob-key tenant-isolation proof runs through a real provider.
            Directory.CreateDirectory(storageDir);
        }

        if (extraSettings is not null)
        {
            foreach (var kv in extraSettings)
                settings[kv.Key] = kv.Value;
        }

        // The per-host IHostEnvironment drives posture and provider safety checks; default "Test" is non-development.
        var builder = KoanIntegrationHost.Configure()
            .WithEnvironment(environment)
            .WithSettings(settings)
            .ConfigureServices(s =>
            {
                s.AddKoan();
                // The deliberately non-isolating fake (inert unless a source names "fake-noniso") — the fail-closed
                // safety-net counter-example now that every real KV adapter announces isolation (ARCH-0103).
                s.AddSingleton<IDataAdapterFactory, NonIsolatingFakeAdapterFactory>();
            });
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
