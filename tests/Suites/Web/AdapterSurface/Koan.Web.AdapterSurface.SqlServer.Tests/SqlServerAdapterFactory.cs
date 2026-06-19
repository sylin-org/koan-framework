using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.SqlServer.Tests;

public sealed class SqlServerAdapterFactory : AdapterTestFactoryBase
{
    private readonly SqlServerContainerHelper _sqlServer = new();

    public override bool IsAvailable => _sqlServer.IsAvailable;
    public override string? UnavailableReason => _sqlServer.UnavailableReason;

    protected override async ValueTask StartBackingStoreAsync() => await _sqlServer.InitializeAsync();
    protected override async ValueTask StopBackingStoreAsync() => await _sqlServer.DisposeAsync();
    public override Task ResetAsync() => _sqlServer.ResetAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Development",
        ["Koan:AllowMagicInProduction"] = "true",
        ["Koan:Data:Sources:Default:Adapter"] = "sqlserver",
        ["Koan:Data:Sources:Default:ConnectionString"] = _sqlServer.ConnectionString,
        ["Koan:Data:SqlServer:ConnectionString"] = _sqlServer.ConnectionString,
        ["Koan:Data:SqlServer:DdlPolicy"] = "AutoCreate",
        ["Koan:Data:Relational:Materialization:FailOnMismatch"] = "false",
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };
}
