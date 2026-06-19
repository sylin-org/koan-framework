using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.AdapterSurface.TestKit.Containers;

namespace Koan.Web.AdapterSurface.Mongo.Tests;

/// <summary>
/// The original-bug adapter: EntityController&lt;Widget&gt; (with a nested Sightings collection)
/// backed by Mongo, exercised over a full HTTP pipeline.
/// </summary>
public sealed class MongoAdapterFactory : AdapterTestFactoryBase
{
    private readonly MongoContainerHelper _mongo = new();

    public override bool IsAvailable => _mongo.IsAvailable;
    public override string? UnavailableReason => _mongo.UnavailableReason;
    protected override string HostEnvironment => "Test";

    protected override async ValueTask StartBackingStoreAsync() => await _mongo.InitializeAsync();
    protected override async ValueTask StopBackingStoreAsync() => await _mongo.DisposeAsync();
    public override Task ResetAsync() => _mongo.ResetAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Test",
        ["Koan:Data:Sources:Default:Adapter"] = "mongo",
        ["Koan:Data:Sources:Default:ConnectionString"] = _mongo.ConnectionString,
        ["Koan:Data:Sources:Default:Database"] = _mongo.Database,
        ["Koan:Data:Mongo:ConnectionString"] = _mongo.ConnectionString,
        ["Koan:Data:Mongo:Database"] = _mongo.Database,
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };
}
