using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Classification.Tests.Support;

/// <summary>
/// A no-Docker integration host (ARCH-0079) that boots a real <c>AddKoan()</c> with the SQLite adapter AND the
/// <c>Koan.Classification</c> module referenced — so the auto-registrar is discovered, its <c>Start</c> activates
/// the host-owned field-transform contributor. Proves classification through real
/// reflective discovery + the live data chokepoint, not a fake. Exposes <see cref="DbPath"/> so a test can read the
/// raw stored bytes and assert ciphertext at rest.
/// </summary>
internal sealed class ClassificationRuntimeFixture : IAsyncDisposable
{
    private readonly IntegrationHost _host;
    private readonly string _rootPath;

    private ClassificationRuntimeFixture(IntegrationHost host, string rootPath, string dbPath)
    {
        _host = host;
        _rootPath = rootPath;
        DbPath = dbPath;
    }

    public IServiceProvider Services => _host.Services;

    /// <summary>The SQLite file backing the default source — for raw at-rest inspection.</summary>
    public string DbPath { get; }

    public static async Task<ClassificationRuntimeFixture> CreateAsync(
        IReadOnlyDictionary<string, string?>? extra = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "Koan-Classification", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "classification.db");

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Development",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            // The SQLite connector reads its connection string from its own key (Koan:Data:Sqlite:ConnectionString),
            // not the source-level one — set it so each test gets its own file (otherwise it falls back to a shared
            // auto path and the raw at-rest read finds nothing).
            ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={dbPath}",
        };
        if (extra is not null)
            foreach (var kv in extra) settings[kv.Key] = kv.Value;

        var host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Development")
            .WithSettings(settings)
            .ConfigureServices(services =>
            {
                configureServices?.Invoke(services);
                services.AddKoan();
            })
            .StartAsync()
            .ConfigureAwait(false);

        AppHost.Current = host.Services;
        return new ClassificationRuntimeFixture(host, root, dbPath);
    }

    /// <summary>Re-bind the host and reset the per-type data-config caches so each fixture boot is clean.</summary>
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

        try
        {
            if (Directory.Exists(_rootPath)) Directory.Delete(_rootPath, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
