using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Couchbase.Tests;

public sealed class CouchbaseAdapterFactory : AdapterTestFactoryBase
{
    private readonly CouchbaseContainerHelper _couchbase = new();

    public override bool IsAvailable => _couchbase.IsAvailable;
    public override string? UnavailableReason => _couchbase.UnavailableReason;

    protected override async ValueTask StartBackingStoreAsync() => await _couchbase.InitializeAsync();
    protected override async ValueTask StopBackingStoreAsync() => await _couchbase.DisposeAsync();
    public override Task ResetAsync() => _couchbase.ResetAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Development",
        ["Koan:Data:Sources:Default:Adapter"] = "couchbase",
        ["Koan:Data:Sources:Default:ConnectionString"] = _couchbase.ConnectionString,
        ["Koan:Data:Couchbase:ConnectionString"] = _couchbase.ConnectionString,
        ["Koan:Data:Couchbase:ManagementUrl"] = _couchbase.ManagementUrl,
        ["Koan:Data:Couchbase:Bucket"] = _couchbase.Bucket,
        ["Koan:Data:Couchbase:Username"] = _couchbase.Username,
        ["Koan:Data:Couchbase:Password"] = _couchbase.Password,
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };
}
