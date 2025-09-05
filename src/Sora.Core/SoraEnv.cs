using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sora.Core;

// Single, immutable runtime snapshot available statically
public static class SoraEnv
{
    /// <summary>
    /// Dumps the current environment snapshot to the console for diagnostics.
    /// </summary>
    public static void DumpSnapshot()
    {
        var snap = Current;
        Console.WriteLine("[SoraEnv][INFO] Environment snapshot:");
        Console.WriteLine($"  EnvironmentName: {snap.EnvironmentName}");
        Console.WriteLine($"  IsDevelopment: {snap.IsDevelopment}");
        Console.WriteLine($"  IsProduction: {snap.IsProduction}");
        Console.WriteLine($"  IsStaging: {snap.IsStaging}");
        Console.WriteLine($"  InContainer: {snap.InContainer}");
        Console.WriteLine($"  IsCi: {snap.IsCi}");
        Console.WriteLine($"  AllowMagicInProduction: {snap.AllowMagicInProduction}");
        Console.WriteLine($"  ProcessStart: {snap.ProcessStart:O}");
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
            // Colon-names (legacy Sora config)
            Configuration.Read(cfg, Infrastructure.Constants.Configuration.Env.DotnetRunningInContainer, false)
            // Underscore-names (standard Docker/K8s)
            || Configuration.Read(cfg, "DOTNET_RUNNING_IN_CONTAINER", false)
            // Colon-names (legacy Sora config)
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.KubernetesServiceHost, null))
            // Underscore-names (standard Docker/K8s)
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, "KUBERNETES_SERVICE_HOST", null));
        bool isCi = Configuration.Read(cfg, Infrastructure.Constants.Configuration.Env.Ci, false)
                     || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.TfBuild, null));
        // Read flag with precedence (env var overrides config) via SoraConfig
        bool magic = Configuration.Read(
                cfg,
                Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction,
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
