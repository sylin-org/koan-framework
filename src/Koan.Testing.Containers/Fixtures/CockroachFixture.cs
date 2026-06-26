using System.Collections.Generic;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0094 CockroachDB container fixture — the Forge dogfood (the framework extends itself). CockroachDB speaks the
/// PostgreSQL wire protocol, so the Cockroach connector is a thin delta over the Postgres adapter (Npgsql + the same
/// SQL dialect). No dedicated Testcontainers module ships for CockroachDB, so this fixture uses the generic
/// <see cref="ContainerBuilder"/>: a single-node insecure instance (no auth, no TLS) exposing pg-wire on 26257 and the
/// admin HTTP on 8080, waited-on via <c>/health?ready=1</c> (200 once the node is accepting SQL).
/// </summary>
public sealed class CockroachFixture : KoanContainerFixture
{
    // CockroachDB pg-wire (SQL) port and admin HTTP port (used only for the readiness probe).
    private const int SqlPort = 26257;
    private const int HttpPort = 8080;

    private IContainer? _container;

    public override string Engine => "cockroach";
    protected override string Adapter => "cockroach";

    protected override async Task<string> StartContainerAsync()
    {
        // v23.2.x is a stable, widely-cached tag that pulls reliably. start-single-node --insecure brings up a
        // ready-to-query node with the `root` superuser, no password, and TLS disabled.
        _container = new ContainerBuilder("cockroachdb/cockroach:v23.2.4")
            .WithCommand("start-single-node", "--insecure")
            .WithPortBinding(SqlPort, true)
            .WithPortBinding(HttpPort, true)
            // /health?ready=1 flips to 200 once the node has joined and is accepting SQL connections — the right
            // "ready" signal. HTTP-from-host works because the single-node container binds the port directly.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(req => req.ForPort(HttpPort).ForPath("/health").ForStatusCode(System.Net.HttpStatusCode.OK)))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);

        var mappedSqlPort = _container.GetMappedPublicPort(SqlPort);
        // Npgsql connection to the insecure node: root superuser, no password, TLS off, default database.
        return $"Host=localhost;Port={mappedSqlPort};Username=root;Database=defaultdb;SSL Mode=Disable";
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("Koan:Data:Cockroach:ConnectionString", connectionString),
        // Testcontainers already waited for readiness; Koan's per-boot readiness gating is redundant here and churns
        // across the suite's rapid host boot/dispose cycles (mirrors PostgresFixture).
        new KeyValuePair<string, string?>("Koan:Data:Cockroach:Readiness:EnableReadinessGating", "false"),
    };
}
