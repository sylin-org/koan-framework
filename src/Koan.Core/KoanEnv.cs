using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Core;

// Single, immutable runtime snapshot available statically
public static class KoanEnv
{
    /// <summary>
    /// Dumps the current environment snapshot using proper logging.
    /// </summary>
    public static void DumpSnapshot(ILogger? logger = null)
    {
        var snap = Current;
        
        if (logger != null)
        {
            logger.LogInformation("Environment snapshot:");
            logger.LogInformation("EnvironmentName: {EnvironmentName}", snap.EnvironmentName);
            logger.LogInformation("IsDevelopment: {IsDevelopment}", snap.IsDevelopment);
            logger.LogInformation("IsProduction: {IsProduction}", snap.IsProduction);
            logger.LogInformation("IsStaging: {IsStaging}", snap.IsStaging);
            logger.LogInformation("InContainer: {InContainer}", snap.InContainer);
            logger.LogInformation("IsCi: {IsCi}", snap.IsCi);
            logger.LogInformation("AllowMagicInProduction: {AllowMagicInProduction}", snap.AllowMagicInProduction);
            logger.LogInformation("ProcessStart: {ProcessStart:O}", snap.ProcessStart);
        }
        else
        {
            // Fallback to console output if no logger available
            Console.WriteLine("[KoanEnv][INFO] Environment snapshot:");
            Console.WriteLine($"  EnvironmentName: {snap.EnvironmentName}");
            Console.WriteLine($"  IsDevelopment: {snap.IsDevelopment}");
            Console.WriteLine($"  IsProduction: {snap.IsProduction}");
            Console.WriteLine($"  IsStaging: {snap.IsStaging}");
            Console.WriteLine($"  InContainer: {snap.InContainer}");
            Console.WriteLine($"  IsCi: {snap.IsCi}");
            Console.WriteLine($"  AllowMagicInProduction: {snap.AllowMagicInProduction}");
            Console.WriteLine($"  ProcessStart: {snap.ProcessStart:O}");
        }
    }

    private static readonly object _gate = new();
    private static volatile bool _initialized;
    private static Snapshot? _snap;

    public static void Initialize(IConfiguration? cfg, IHostEnvironment? env)
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;
            _snap = ComputeSnapshot(cfg, env);
            _initialized = true;
            DumpSnapshot();
        }
    }

    public static void TryInitialize(IServiceProvider sp)
    {
        if (_initialized || sp is null) return;
        try
        {
            var cfg = sp.GetService(typeof(IConfiguration)) as IConfiguration;
            var env = sp.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
            Initialize(cfg, env);
        }
        catch { /* best effort */ }
    }

    private static Snapshot ComputeSnapshot(IConfiguration? cfg, IHostEnvironment? env)
    {
        var envName = env?.EnvironmentName
            ?? Configuration.ReadFirst(
                cfg,
                Infrastructure.Constants.Configuration.Env.DotnetEnvironment,
                Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment
            )
            ?? string.Empty;
        bool isDev = env?.IsDevelopment() ?? string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);
        bool isProd = env?.IsProduction() ?? string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase);
        bool isStg = env?.IsStaging() ?? string.Equals(envName, "Staging", StringComparison.OrdinalIgnoreCase);
        // Check both colon and underscore env var names for Docker/K8s compatibility
        bool inContainer =
            // Colon-names (legacy Koan config)
            Configuration.Read(cfg, Infrastructure.Constants.Configuration.Env.DotnetRunningInContainer, false)
            // Underscore-names (standard Docker/K8s)
            || Configuration.Read(cfg, "DOTNET_RUNNING_IN_CONTAINER", false)
            // Colon-names (legacy Koan config)
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.KubernetesServiceHost, null))
            // Underscore-names (standard Docker/K8s)
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, "KUBERNETES_SERVICE_HOST", null));
        bool isCi = Configuration.Read(cfg, Infrastructure.Constants.Configuration.Env.Ci, false)
                     || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.TfBuild, null));
        // Read flag with precedence (env var overrides config) via KoanConfig
        bool magic = Configuration.Read(
                cfg,
                Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction,
                false
            );
        return new Snapshot(envName, isDev, isProd, isStg, inContainer, isCi, magic, DateTimeOffset.UtcNow);
    }

    private static Snapshot Current => _initialized && _snap is not null ? _snap : ComputeSnapshot(null, null);

    public static string EnvironmentName => Current.EnvironmentName;
    public static bool IsDevelopment => Current.IsDevelopment;
    public static bool IsProduction => Current.IsProduction;
    public static bool IsStaging => Current.IsStaging;
    public static bool InContainer => Current.InContainer;
    public static bool IsCi => Current.IsCi;
    public static bool AllowMagicInProduction => Current.AllowMagicInProduction;
    public static DateTimeOffset ProcessStart => Current.ProcessStart;

    private sealed record Snapshot(
        string EnvironmentName,
        bool IsDevelopment,
        bool IsProduction,
        bool IsStaging,
        bool InContainer,
        bool IsCi,
        bool AllowMagicInProduction,
        DateTimeOffset ProcessStart
    );
}
