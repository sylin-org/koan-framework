using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Messaging;

public sealed class InboxClientOptions
{
    // Explicit external inbox endpoint; when set, discovery is skipped.
    public string? Endpoint { get; set; }
    // If true and no inbox is resolved, client may fail-closed (future use)
    public bool Required { get; set; } = false;
}

public sealed class DiscoveryOptions
{
    // When null, environment defaults apply (On in non-Production, Off in Production unless Magic flag is set)
    public bool? Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 3;
    public int CacheMinutes { get; set; } = 5;
    // Optional small wait to collect multiple announces and choose the best endpoint
    public int SelectionWaitMs { get; set; } = 150;
}

public interface IInboxDiscoveryPolicy
{
    bool ShouldDiscover(IServiceProvider sp);
    string Reason(IServiceProvider sp);
}

public interface IInboxDiscoveryClient
{
    // Attempts to discover an inbox endpoint for the default bus/group context.
    // Returns a base URL or null if none found within the timeout defined by DiscoveryOptions.
    Task<string?> DiscoverAsync(CancellationToken ct = default);
}

public sealed class InboxDiscoveryPolicy : IInboxDiscoveryPolicy
{
    public bool ShouldDiscover(IServiceProvider sp)
    {
        var cfg = (IConfiguration)sp.GetService(typeof(IConfiguration))!;
        var endpoint = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Endpoint, null);
        if (!string.IsNullOrWhiteSpace(endpoint)) return false; // explicit config wins

        var enabledSetting = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Discovery.Enabled, null);
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase);

        var isProd = IsProduction();
        var magic = Sora.Core.Configuration.Read(cfg, Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        return !isProd || magic;
    }

    public string Reason(IServiceProvider sp)
    {
        var cfg = (IConfiguration)sp.GetService(typeof(IConfiguration))!;
        var endpoint = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Inbox.Endpoint, null);
        if (!string.IsNullOrWhiteSpace(endpoint)) return "explicit-endpoint";
        var enabledSetting = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Discovery.Enabled, null);
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase) ? "enabled-explicit" : "disabled-explicit";
        var isProd = IsProduction();
        var magic = Sora.Core.Configuration.Read(cfg, Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);
        if (isProd && !magic) return "disabled-production-default";
        if (isProd && magic) return "enabled-magic";
        return "enabled-dev-default";
    }

    private static bool IsProduction()
    {
        try { return SoraEnv.IsProduction; } catch { }
        var env = Sora.Core.Configuration.ReadFirst(null, Sora.Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment, Sora.Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment);
        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
    }
}

public static class InboxConfigurationExtensions
{
    public static IServiceCollection AddInboxConfiguration(this IServiceCollection services)
    {
        services.AddOptions<InboxClientOptions>().BindConfiguration("Sora:Messaging:Inbox");
        services.AddOptions<DiscoveryOptions>().BindConfiguration("Sora:Messaging:Discovery");
        services.TryAddSingleton<IInboxDiscoveryPolicy, InboxDiscoveryPolicy>();
        return services;
    }
}
