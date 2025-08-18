using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Messaging.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Messaging.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddMessagingCore();
        services.AddInboxConfiguration();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var m = new Sora.Messaging.MessagingOptions();
        cfg.GetSection("Sora:Messaging").Bind(m);
        report.AddSetting("DefaultBus", m.DefaultBus);
        report.AddSetting("DefaultGroup", m.DefaultGroup);
        report.AddSetting("IncludeVersionInAlias", m.IncludeVersionInAlias.ToString());
        var discEnabled = cfg["Sora:Messaging:Discovery:Enabled"];
        report.AddSetting("Discovery.Enabled", discEnabled);
        var inboxEndpoint = cfg["Sora:Messaging:Inbox:Endpoint"];
        report.AddSetting("Inbox.Endpoint", inboxEndpoint, isSecret: false);
    }
}
