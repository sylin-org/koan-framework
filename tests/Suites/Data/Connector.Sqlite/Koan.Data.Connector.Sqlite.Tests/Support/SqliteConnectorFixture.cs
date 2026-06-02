using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing;
using Koan.Testing.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Sqlite.Tests.Support;

/// <summary>
/// Dockerless live fixture for the SQLite relational adapter on the current test harness
/// (mirrors <c>InMemoryConnectorFixture</c>). Backs the default data source with a unique temp-file
/// SQLite database so each fixture is isolated; the file is deleted on dispose. Used via
/// <c>TestPipeline.Using&lt;SqliteConnectorFixture&gt;(...)</c>.
/// </summary>
internal sealed class SqliteConnectorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _dbPath;

    private SqliteConnectorFixture(ServiceProvider provider, IDataService data, IConfiguration configuration, string dbPath)
    {
        _provider = provider;
        Data = data;
        Configuration = configuration;
        _dbPath = dbPath;
    }

    public IServiceProvider Services => _provider;
    public IDataService Data { get; }
    public IConfiguration Configuration { get; }

    public static ValueTask<SqliteConnectorFixture> Create(TestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var dbPath = Path.Combine(Path.GetTempPath(), $"koan-sqlite-conv-{Guid.NewGuid():n}.db");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={dbPath}"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();
        services.AddSingleton<IHostEnvironment, TestHostEnvironment>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKoan();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false
        });

        try { KoanEnv.TryInitialize(provider); }
        catch { /* KoanEnv is sticky per-process; ignore duplicate init. */ }

        AppHost.Current = provider;
        var data = provider.GetRequiredService<IDataService>();
        return ValueTask.FromResult(new SqliteConnectorFixture(provider, data, configuration, dbPath));
    }

    public void BindHost() => AppHost.Current = _provider;

    public async ValueTask DisposeAsync()
    {
        if (ReferenceEquals(AppHost.Current, _provider)) AppHost.Current = null;
        try { await _provider.DisposeAsync().ConfigureAwait(false); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort temp cleanup */ }
    }

    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Koan.Data.Connector.Sqlite.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
