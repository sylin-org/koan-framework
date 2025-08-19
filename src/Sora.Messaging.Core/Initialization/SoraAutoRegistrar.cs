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
    // Read settings via Configuration helper (ADR-0040)
    var defBus = Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Keys.DefaultBus, null);
    var defGroup = Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Keys.DefaultGroup, null);
    var includeAliasVer = Configuration.Read(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Keys.IncludeVersionInAlias, false);
    report.AddSetting("DefaultBus", defBus);
    report.AddSetting("DefaultGroup", defGroup);
    report.AddSetting("IncludeVersionInAlias", includeAliasVer.ToString());
    var discEnabled = Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Discovery.Enabled, null);
    report.AddSetting("Discovery.Enabled", discEnabled);
    var inboxEndpoint = Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Endpoint, null);
    report.AddSetting("Inbox.Endpoint", inboxEndpoint, isSecret: false);
    }
}
