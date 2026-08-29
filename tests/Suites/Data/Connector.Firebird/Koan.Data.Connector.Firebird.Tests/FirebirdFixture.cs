using DotNet.Testcontainers.Builders;
using Testcontainers.FirebirdSql;

namespace Koan.Data.Connector.Firebird.Tests;

/// <summary>
/// Firebird 5.0 container fixture. Three image defaults are hostile to managed clients and are set
/// here: the wire requires encryption the FirebirdClient cannot negotiate (WireCrypt=Enabled), the
/// default Srp256-only auth set excludes the Srp plugin the client speaks (AuthServer="Srp256, Srp"),
/// and the SYSDBA password is set by FIREBIRD_ROOT_PASSWORD, not ISC_PASSWORD. The official image also
/// carries no HEALTHCHECK, so the module's container-healthy wait would never answer — the fixture
/// waits on the internal Firebird port instead and lets the module's isql connection-string probe be
/// the real readiness gate. The database file is created by the image entrypoint at start.
/// </summary>
public sealed class FirebirdFixture : KoanContainerFixture
{
    private const string Image = "firebirdsql/firebird:5.0.4";
    private const string Database = "/var/lib/firebird/data/koan.fdb";
    private const string Password = "koan";
    private const ushort Port = 3050;

    private FirebirdSqlContainer? _container;

    public override string Engine => "firebird";
    protected override string Adapter => "firebird";

    protected override async Task<string> StartContainerAsync()
    {
        _container = new FirebirdSqlBuilder(Image)
            .WithDatabase(Database)
            .WithUsername("SYSDBA")
            .WithPassword(Password)
            .WithEnvironment("FIREBIRD_ROOT_PASSWORD", Password)
            .WithEnvironment("FIREBIRD_CONF_WireCrypt", "Enabled")
            .WithEnvironment("FIREBIRD_CONF_AuthServer", "Srp256, Srp")
            .WithEnvironment("FIREBIRD_CONF_AuthClient", "Srp256, Srp")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(Port))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) =>
    [
        new("Koan:Data:Firebird:ConnectionString", connectionString),
        new("Koan:Data:Firebird:Readiness:EnableReadinessGating", "false"),
    ];
}
