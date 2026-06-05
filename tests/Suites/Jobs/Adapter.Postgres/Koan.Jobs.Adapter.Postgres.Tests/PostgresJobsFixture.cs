using Testcontainers.PostgreSql;
using Xunit;

namespace Koan.Jobs.Adapter.Postgres.Tests;

/// <summary>Starts a Postgres container once for the class and exposes the Koan data-source settings.</summary>
public sealed class PostgresJobsFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public IReadOnlyDictionary<string, string?> Settings { get; private set; } = new Dictionary<string, string?>();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("koan_jobs")
            .WithUsername("koan")
            .WithPassword("koan")
            .Build();
        await _container.StartAsync();
        Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Sources:Default:Adapter"] = "postgres",
            ["Koan:Data:Sources:Default:ConnectionString"] = _container.GetConnectionString(),
        };
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
