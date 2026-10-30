using Koan.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// The application's store for one spec: a Koan host over a SQLite file of its own, in Development, and the key ring
/// every process of the application shares. Disposing it stops the host and deletes the folder.
/// </summary>
internal sealed class TestStore : IAsyncDisposable
{
    private const string ApplicationName = "atproto-specs";
    private readonly IHost _host;

    private TestStore(IHost host, string folder, string adapter) { _host = host; Folder = folder; Adapter = adapter; }

    public string Folder { get; }

    /// <summary>The data connector this store elected: <c>sqlite</c> by default, or <c>duckdb</c>, which replaces rows
    /// conditionally and declares no conditional delete.</summary>
    public string Adapter { get; }

    public IServiceProvider Services => _host.Services;

    /// <summary>
    /// Starts the store over <paramref name="adapter"/>; <paramref name="declare"/> runs inside the host's
    /// <c>AddKoan</c> composition, where entity lifecycle hooks are declared.
    /// </summary>
    public static async Task<TestStore> Start(string adapter = "sqlite", Action? declare = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "koan-atproto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // The first key is written before any process reads the ring, as an application's first start does.
        Ring(folder).CreateProtector("ring").Protect("first key");
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Development",
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Koan:Data:Sources:Default:Adapter"] = adapter,
            ["Koan:Data:Sources:Default:ConnectionString"] = adapter == "sqlite"
                ? $"Data Source={Path.Combine(folder, "store.sqlite")};Pooling=False"
                : $"Data Source={Path.Combine(folder, "store." + adapter)}",
        };
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Development");
                web.UseContentRoot(AppContext.BaseDirectory);
                web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                web.ConfigureServices(services =>
                {
                    if (declare is null) services.AddKoan();
                    else services.AddKoan(declare);
                });
                web.Configure(_ => { });
            })
            .Build();
        try { await host.StartAsync(TestContext.Current.CancellationToken); }
        catch { host.Dispose(); throw; }
        return new TestStore(host, folder, adapter);
    }

    /// <summary>The tables of the store's SQLite file, read past Koan, so a spec names the store that answered.</summary>
    public async Task<IReadOnlyList<string>> Tables()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(Folder, "store.sqlite")};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken)) tables.Add(reader.GetString(0));
        return tables;
    }

    /// <summary>One process's view of the application's key ring.</summary>
    public IDataProtectionProvider Protection() => Ring(Folder);

    /// <summary>A key ring this application never had, as after its keys were lost.</summary>
    public IDataProtectionProvider LostRing()
        => DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Folder, "lost-keys")), ring => ring.SetApplicationName(ApplicationName));

    private static IDataProtectionProvider Ring(string folder)
        => DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")), ring => ring.SetApplicationName(ApplicationName));

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
