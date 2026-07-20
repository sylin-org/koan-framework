using Koan.Jobs;
using Koan.Jobs.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Xunit;

namespace Koan.Jobs.Adapter.SqlServer.Tests;

/// <summary>Starts a SQL Server container once for the class and exposes the Koan data-source settings.</summary>
public sealed class SqlServerJobsFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    public IReadOnlyDictionary<string, string?> Settings { get; private set; } = new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU6-GDR1-ubuntu-24.04").Build();
        await _container.StartAsync();
        var cs = _container.GetConnectionString();
        Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlserver",
            ["Koan:Data:Sources:Default:ConnectionString"] = cs,
            ["Koan:Data:SqlServer:ConnectionString"] = cs,
            // Testcontainers already waits for the container; skip Koan's per-boot readiness gating so the
            // 28 rapid host boot/dispose cycles don't intermittently exceed the 30s readiness window.
            ["Koan:Data:SqlServer:Readiness:EnableReadinessGating"] = "false",
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

/// <summary>Runs the shared <see cref="JobBehaviorSuite"/> on a real SQL Server store.</summary>
public sealed class SqlServerBehaviors : JobBehaviorSuite, IClassFixture<SqlServerJobsFixture>
{
    private readonly SqlServerJobsFixture _fx;
    public SqlServerBehaviors(SqlServerJobsFixture fx) => _fx = fx;

    protected override Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
        => JobsHarness.StartWithSettingsAsync(_fx.Settings, configure, configureServices);
}
