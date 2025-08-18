using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sora.Core;

// Single, immutable runtime snapshot available statically
public static class SoraEnv
{
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
            ?? Sora.Core.Configuration.ReadFirst(
                cfg,
                Sora.Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment,
                Sora.Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment
            )
            ?? string.Empty;
        bool isDev = env?.IsDevelopment() ?? string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);
        bool isProd = env?.IsProduction() ?? string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase);
        bool isStg = env?.IsStaging() ?? string.Equals(envName, "Staging", StringComparison.OrdinalIgnoreCase);
    bool inContainer = Sora.Core.Configuration.Read(
                cfg,
                Sora.Core.Infrastructure.Constants.Configuration.Env.DotnetRunningInContainer,
                false)
            || !string.IsNullOrEmpty(Sora.Core.Configuration.Read<string?>(cfg, Sora.Core.Infrastructure.Constants.Configuration.Env.KubernetesServiceHost, null));
    bool isCi = Sora.Core.Configuration.Read(cfg, Sora.Core.Infrastructure.Constants.Configuration.Env.Ci, false)
                 || !string.IsNullOrEmpty(Sora.Core.Configuration.Read<string?>(cfg, Sora.Core.Infrastructure.Constants.Configuration.Env.TfBuild, null));
        // Read flag with precedence (env var overrides config) via SoraConfig
    bool magic = Sora.Core.Configuration.Read(
            cfg,
            Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction,
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
