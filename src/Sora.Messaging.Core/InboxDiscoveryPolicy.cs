using Microsoft.Extensions.Configuration;
using Sora.Core;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging;

public sealed class InboxDiscoveryPolicy : IInboxDiscoveryPolicy
{
    public bool ShouldDiscover(IServiceProvider sp)
    {
        var cfg = (IConfiguration)sp.GetService(typeof(IConfiguration))!;
        var endpoint = Configuration.Read<string?>(cfg, Constants.Configuration.Inbox.Endpoint, null);
        if (!string.IsNullOrWhiteSpace(endpoint)) return false; // explicit config wins

        var enabledSetting = Configuration.Read<string?>(cfg, Constants.Configuration.Discovery.Enabled, null);
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase);

        var isProd = IsProduction();
        var magic = Configuration.Read(cfg, Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        return !isProd || magic;
    }

    public string Reason(IServiceProvider sp)
    {
        var cfg = (IConfiguration)sp.GetService(typeof(IConfiguration))!;
        var endpoint = Configuration.Read<string?>(cfg, Constants.Configuration.Inbox.Endpoint, null);
        if (!string.IsNullOrWhiteSpace(endpoint)) return "explicit-endpoint";
        var enabledSetting = Configuration.Read<string?>(cfg, Constants.Configuration.Discovery.Enabled, null);
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase) ? "enabled-explicit" : "disabled-explicit";
        var isProd = IsProduction();
        var magic = Configuration.Read(cfg, Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        if (isProd && !magic) return "disabled-production-default";
        if (isProd && magic) return "enabled-magic";
        return "enabled-dev-default";
    }

    private static bool IsProduction()
    {
        try { return SoraEnv.IsProduction; } catch { }
        var env = Configuration.ReadFirst(null, Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment, Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment);
        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
    }
}