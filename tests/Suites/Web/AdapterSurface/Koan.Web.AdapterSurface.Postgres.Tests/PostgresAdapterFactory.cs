using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Postgres.Tests;

public sealed class PostgresAdapterFactory : AdapterTestFactoryBase
{
    private readonly PostgresContainerHelper _pg = new();

    public override bool IsAvailable => _pg.IsAvailable;
    public override string? UnavailableReason => _pg.UnavailableReason;

    protected override async ValueTask StartBackingStoreAsync() => await _pg.InitializeAsync();
    protected override async ValueTask StopBackingStoreAsync() => await _pg.DisposeAsync();
    public override Task ResetAsync() => _pg.ResetAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Development",
        ["Koan:AllowMagicInProduction"] = "true",
        ["Koan:Data:Sources:Default:Adapter"] = "postgres",
        ["Koan:Data:Sources:Default:ConnectionString"] = _pg.ConnectionString,
        ["Koan:Data:Postgres:ConnectionString"] = _pg.ConnectionString,
        ["Koan:Data:Postgres:DdlPolicy"] = "AutoCreate",
        ["Koan:Data:Relational:Materialization:FailOnMismatch"] = "false",
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };
}
