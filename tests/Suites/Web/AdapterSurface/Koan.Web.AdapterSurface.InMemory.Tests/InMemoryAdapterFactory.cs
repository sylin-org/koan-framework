using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Hosting.App;
using Koan.Web.AdapterSurface.InMemory.Tests.PredicateHook;
using Koan.Web.AdapterSurface.TestKit;
using Koan.Web.Extensions;
using Koan.Web.Hooks;

namespace Koan.Web.AdapterSurface.InMemory.Tests;

public sealed class InMemoryAdapterFactory : AdapterTestFactoryBase
{
    public override bool IsAvailable => true;
    protected override string HostEnvironment => "Test";

    // The PredicateHook specs exercise VisibilityWidgetController + VisibilityHook (declared in this
    // test assembly). The direct host doesn't scan the entry assembly the way the old WAF host did, so
    // register both explicitly.
    protected override void ConfigureAdditionalServices(IServiceCollection services)
    {
        services.AddKoanControllersFrom<VisibilityWidgetController>();
        services.AddSingleton<IRequestOptionsHook<VisibilityWidget>, VisibilityHook>();
    }

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Test",
        ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
        ["Koan:Data:Sources:Default:ConnectionString"] = "memory://adapter-surface",
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };

    public override async Task ResetAsync()
    {
        AppHost.Current = Services;
        await Widget.RemoveAll();
    }
}
