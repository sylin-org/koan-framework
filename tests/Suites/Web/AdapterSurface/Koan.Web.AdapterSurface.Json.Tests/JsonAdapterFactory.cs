using Koan.Core.Hosting.App;
using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Json.Tests;

public sealed class JsonAdapterFactory : AdapterTestFactoryBase
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"koan-json-surface-{Guid.NewGuid():N}");

    public override bool IsAvailable => true;
    protected override string HostEnvironment => "Test";

    protected override ValueTask StartBackingStoreAsync()
    {
        Directory.CreateDirectory(_dataDir);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask StopBackingStoreAsync()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
        return ValueTask.CompletedTask;
    }

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Test",
        ["Koan:Data:Sources:Default:Adapter"] = "json",
        ["Koan:Data:Sources:Default:DirectoryPath"] = _dataDir,
        ["Koan:Data:Json:DirectoryPath"] = _dataDir,
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };

    public override async Task ResetAsync()
    {
        AppHost.Current = Services;
        await Widget.RemoveAll();
    }
}
