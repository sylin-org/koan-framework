using Testcontainers.PostgreSql;
using Xunit;

namespace Koan.Jobs.Adapter.Postgres.Tests;

/// <summary>Starts a Postgres container once for the class and exposes the Koan data-source settings.</summary>
public sealed class PostgresJobsFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public IReadOnlyDictionary<string, string?> Settings { get; private set; } = new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:18.4-alpine")
            .WithDatabase("koan_jobs")
            .WithUsername("koan")
            .WithPassword("koan")
            .Build();
        await _container.StartAsync();
        var cs = _container.GetConnectionString();
        Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "postgres",
            ["Koan:Data:Sources:Default:ConnectionString"] = cs,
            ["Koan:Data:Postgres:ConnectionString"] = cs,
            // Testcontainers already waits for the container; Koan's per-boot readiness gating is redundant here and,
            // across the suite's rapid host boot/dispose cycles, intermittently exceeds its window → connection
            // churn and cascading failures. Disable it, matching the Mongo/SqlServer fixtures.
            ["Koan:Data:Postgres:Readiness:EnableReadinessGating"] = "false",
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
