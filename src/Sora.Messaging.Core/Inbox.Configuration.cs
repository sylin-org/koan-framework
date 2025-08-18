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
        var endpoint = cfg["Sora:Messaging:Inbox:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint)) return false; // explicit config wins

        var enabledSetting = cfg["Sora:Messaging:Discovery:Enabled"];
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase);

        var isProd = IsProduction();
        var magic = string.Equals(cfg["Sora:AllowMagicInProduction"] ?? "false", "true", StringComparison.OrdinalIgnoreCase);
        return !isProd || magic;
    }

    public string Reason(IServiceProvider sp)
    {
        var cfg = (IConfiguration)sp.GetService(typeof(IConfiguration))!;
        var endpoint = cfg["Sora:Messaging:Inbox:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint)) return "explicit-endpoint";
        var enabledSetting = cfg["Sora:Messaging:Discovery:Enabled"];
        if (!string.IsNullOrWhiteSpace(enabledSetting))
            return string.Equals(enabledSetting, "true", StringComparison.OrdinalIgnoreCase) ? "enabled-explicit" : "disabled-explicit";
        var isProd = IsProduction();
        var magic = string.Equals(cfg["Sora:AllowMagicInProduction"] ?? "false", "true", StringComparison.OrdinalIgnoreCase);
        if (isProd && !magic) return "disabled-production-default";
        if (isProd && magic) return "enabled-magic";
        return "enabled-dev-default";
    }

    private static bool IsProduction()
    {
    try { return SoraEnv.IsProduction; } catch { }
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
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
