using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Redis.Tests;

public sealed class RedisAdapterFactory : AdapterTestFactoryBase
{
    private readonly RedisContainerHelper _redis = new();

    public override bool IsAvailable => _redis.IsAvailable;
    public override string? UnavailableReason => _redis.UnavailableReason;
    protected override string HostEnvironment => "Test";

    protected override async ValueTask StartBackingStoreAsync() => await _redis.InitializeAsync();
    protected override async ValueTask StopBackingStoreAsync() => await _redis.DisposeAsync();
    public override Task ResetAsync() => _redis.ResetAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Test",
        ["Koan:Data:Sources:Default:Adapter"] = "redis",
        ["Koan:Data:Sources:Default:ConnectionString"] = _redis.ConnectionString,
        ["ConnectionStrings:Redis"] = _redis.ConnectionString,
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };
}
