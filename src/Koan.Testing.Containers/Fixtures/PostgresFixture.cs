using System.Collections.Generic;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 Postgres container fixture — the canonical engine fixture shape. Starts the official
/// <see cref="PostgreSqlContainer"/> module and hands its connection string to the Koan data layer.
/// </summary>
public sealed class PostgresFixture : KoanContainerFixture
{
    private PostgreSqlContainer? _container;

    public override string Engine => "postgres";
    protected override string Adapter => "postgres";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("koan")
            .WithUsername("koan")
            .WithPassword("koan")
            .Build();
        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("Koan:Data:Postgres:ConnectionString", connectionString),
        // Testcontainers already waited for readiness; Koan's per-boot readiness gating is redundant here
        // and churns across the suite's rapid host boot/dispose cycles (JOBS-0005 PostgresJobsFixture lesson).
        new KeyValuePair<string, string?>("Koan:Data:Postgres:Readiness:EnableReadinessGating", "false"),
    };
}
