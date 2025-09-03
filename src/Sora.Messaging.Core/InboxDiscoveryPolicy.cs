
using Microsoft.Extensions.Configuration;
using Sora.Core;
using Sora.Messaging.Infrastructure;
using CoreConstants = Sora.Core.Infrastructure.Constants;
using MessagingConstants = Sora.Messaging.Infrastructure.Constants;

namespace Sora.Messaging;

public sealed class InboxDiscoveryPolicy : IInboxDiscoveryPolicy
{
    public bool ShouldDiscover(IServiceProvider sp)
    {
        var cfg = (IConfiguration)sp.GetService(typeof(IConfiguration))!;
    var endpoint = Configuration.Read<string?>(cfg, MessagingConstants.Configuration.Inbox.Endpoint, null);
        if (!string.IsNullOrWhiteSpace(endpoint)) return false; // explicit config wins

    var enabledSetting = Configuration.Read<string?>(cfg, MessagingConstants.Configuration.Discovery.Enabled, null);
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase);

        var isProd = IsProduction();
    var magic = Configuration.Read(cfg, CoreConstants.Configuration.Sora.AllowMagicInProduction, false);
        return !isProd || magic;
    }

    public string Reason(IServiceProvider sp)
    {
        var cfg = (IConfiguration)sp.GetService(typeof(IConfiguration))!;
    var endpoint = Configuration.Read<string?>(cfg, MessagingConstants.Configuration.Inbox.Endpoint, null);
        if (!string.IsNullOrWhiteSpace(endpoint)) return "explicit-endpoint";
    var enabledSetting = Configuration.Read<string?>(cfg, MessagingConstants.Configuration.Discovery.Enabled, null);
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase) ? "enabled-explicit" : "disabled-explicit";
        var isProd = IsProduction();
    var magic = Configuration.Read(cfg, CoreConstants.Configuration.Sora.AllowMagicInProduction, false);
        if (isProd && !magic) return "disabled-production-default";
        if (isProd && magic) return "enabled-magic";
        return "enabled-dev-default";
    }

    private static bool IsProduction()
    {
        try { return SoraEnv.IsProduction; } catch { }
    var env = Configuration.ReadFirst(null, CoreConstants.Configuration.Env.AspNetCoreEnvironment, CoreConstants.Configuration.Env.DotnetEnvironment);
        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
    }
}