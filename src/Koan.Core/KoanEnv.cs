using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Provenance;
using System.Reflection;

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
            KoanLog.SnapshotInfo(logger, "env.snapshot", null,
                ("environment", snap.EnvironmentName),
                ("isDevelopment", snap.IsDevelopment),
                ("isProduction", snap.IsProduction),
                ("isStaging", snap.IsStaging),
                ("inContainer", snap.InContainer),
                ("isCi", snap.IsCi),
                ("allowMagicInProduction", snap.AllowMagicInProduction),
                ("processStart", snap.ProcessStart.ToString("o")),
                ("orchestrationMode", snap.OrchestrationMode.ToString()),
                ("sessionId", snap.SessionId),
                ("assemblies", KoanAssemblies.Length),
                ("application", snap.Identity.Name),
                ("applicationCode", snap.Identity.Code));
            return;
        }

        // Fallback to console output if no logger available
        Console.WriteLine("[K:SNAP] env.snapshot environment={0} isDevelopment={1} isProduction={2} isStaging={3} inContainer={4} isCi={5} allowMagicInProduction={6} processStart={7:o} orchestrationMode={8} sessionId={9} assemblies={10} application={11} applicationCode={12}",
            snap.EnvironmentName,
            snap.IsDevelopment,
            snap.IsProduction,
            snap.IsStaging,
            snap.InContainer,
            snap.IsCi,
            snap.AllowMagicInProduction,
            snap.ProcessStart,
            snap.OrchestrationMode,
            snap.SessionId,
            KoanAssemblies.Length,
            snap.Identity.Name,
            snap.Identity.Code);
    }

    private static readonly object _gate = new();
    private static volatile bool _initialized;
    private static SnapshotData? _snap;

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

    private static OrchestrationMode DetectOrchestrationMode(IConfiguration? cfg)
    {
        // Priority 1: Forced configuration override (highest priority)
        var forcedModeString = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Orchestration.ForceOrchestrationMode, null);
        if (!string.IsNullOrEmpty(forcedModeString) && Enum.TryParse<OrchestrationMode>(forcedModeString, true, out var forcedMode))
        {
            return forcedMode;
        }

        // Priority 2: Aspire AppHost (external orchestration takes precedence)
        if (IsAspireAppHostContext(cfg))
        {
            return OrchestrationMode.AspireAppHost;
        }

        // Priority 3: Docker Compose (real-time detection)
        if (IsDockerComposeContext(cfg))
        {
            return OrchestrationMode.DockerCompose;
        }

        // Priority 4: Kubernetes (real-time detection - requires strong evidence)
        if (IsKubernetesContext(cfg))
        {
            return OrchestrationMode.Kubernetes;
        }

        // Default: Standalone mode with application-supplied or external dependencies.
        return OrchestrationMode.Standalone;
    }

    private static bool IsAspireAppHostContext(IConfiguration? cfg)
    {
        return !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.AspireResourceName, null)) ||
               !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.AspireUrls, null)) ||
               !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Endpoint, null));
    }

    private static bool IsDockerComposeContext(IConfiguration? cfg)
    {
        return !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.ComposeProjectName, null)) ||
               !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.ComposeService, null)) ||
               !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.ComposeContainerName, null));
    }

    private static bool IsKubernetesContext(IConfiguration? cfg)
    {
        // Kubernetes requires strong evidence - not just hostname patterns
        return !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.KubernetesServiceHost, null)) ||
               !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.KubernetesServicePort, null)) ||
               Directory.Exists("/var/run/secrets/kubernetes.io/serviceaccount");
    }

    private static SnapshotData ComputeSnapshot(IConfiguration? cfg, IHostEnvironment? env)
    {
        var envName = env?.EnvironmentName
            ?? Configuration.ReadFirst(
                cfg,
                Infrastructure.Constants.Configuration.Env.DotnetEnvironment,
                Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment
            )
            ?? "";
        bool isDev = env?.IsDevelopment() ?? string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);
        bool isProd = env?.IsProduction() ?? string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase);
        bool isStg = env?.IsStaging() ?? string.Equals(envName, "Staging", StringComparison.OrdinalIgnoreCase);
        // Check both colon and underscore env var names for Docker/K8s/Compose compatibility
        bool inContainer =
            // Colon-names (legacy Koan config)
            Configuration.Read(cfg, Infrastructure.Constants.Configuration.Env.DotnetRunningInContainer, false)
            // Underscore-names (standard Docker/K8s)
            || Configuration.Read(cfg, "DOTNET_RUNNING_IN_CONTAINER", false)
            // Colon-names (legacy Koan config)
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.KubernetesServiceHost, null))
            // Underscore-names (standard Docker/K8s)
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, "KUBERNETES_SERVICE_HOST", null))
            // Docker Compose environment detection
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.ComposeProjectName, null))
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.ComposeService, null))
            || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.ComposeContainerName, null));
        bool isCi = Configuration.Read(cfg, Infrastructure.Constants.Configuration.Env.Ci, false)
                     || !string.IsNullOrEmpty(Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.TfBuild, null));
        // Read flag with precedence (env var overrides config) via KoanConfig
        bool magic = Configuration.Read(
                cfg,
                Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction,
                false
            );

        // Detect orchestration mode using proper precedence
        var orchestrationMode = DetectOrchestrationMode(cfg);
        var sessionId = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.KoanSessionId, null) ??
                       Guid.NewGuid().ToString("N")[..8];

        var identity = ApplicationIdentityDefaults.Resolve(cfg, env);

        return new SnapshotData(envName, isDev, isProd, isStg, inContainer, isCi, magic, DateTimeOffset.UtcNow, orchestrationMode, sessionId, identity);
    }

    private static SnapshotData Current => _initialized && _snap is not null ? _snap : ComputeSnapshot(null, null);

    public static KoanEnvironmentSnapshot CurrentSnapshot => new(
        Current.EnvironmentName,
        Current.IsDevelopment,
        Current.IsProduction,
        Current.IsStaging,
        Current.InContainer,
        Current.IsCi,
        Current.AllowMagicInProduction,
        Current.ProcessStart,
        Current.OrchestrationMode,
        Current.SessionId,
        AssemblyCache.Instance.GetKoanAssemblies().Length,
        Current.Identity);

    public static string EnvironmentName => CurrentSnapshot.EnvironmentName;
    public static bool IsDevelopment => CurrentSnapshot.IsDevelopment;
    public static bool IsProduction => CurrentSnapshot.IsProduction;
    public static bool IsStaging => CurrentSnapshot.IsStaging;
    public static bool InContainer => CurrentSnapshot.InContainer;
    public static bool IsCi => CurrentSnapshot.IsCi;
    public static bool AllowMagicInProduction => CurrentSnapshot.AllowMagicInProduction;
    public static DateTimeOffset ProcessStart => CurrentSnapshot.ProcessStart;
    public static OrchestrationMode OrchestrationMode => CurrentSnapshot.OrchestrationMode;
    public static string SessionId => CurrentSnapshot.SessionId;
    public static Assembly[] KoanAssemblies => AssemblyCache.Instance.GetKoanAssemblies();
    public static ProvenanceSnapshot Provenance => ProvenanceRegistry.Instance.CurrentSnapshot;

    private sealed record SnapshotData(
        string EnvironmentName,
        bool IsDevelopment,
        bool IsProduction,
        bool IsStaging,
        bool InContainer,
        bool IsCi,
        bool AllowMagicInProduction,
        DateTimeOffset ProcessStart,
        OrchestrationMode OrchestrationMode,
        string SessionId,
        ApplicationIdentitySnapshot Identity);
}

public readonly record struct KoanEnvironmentSnapshot(
    string EnvironmentName,
    bool IsDevelopment,
    bool IsProduction,
    bool IsStaging,
    bool InContainer,
    bool IsCi,
    bool AllowMagicInProduction,
    DateTimeOffset ProcessStart,
    OrchestrationMode OrchestrationMode,
    string SessionId,
    int AssemblyCount,
    ApplicationIdentitySnapshot Application)
{
    public TimeSpan Uptime => DateTimeOffset.UtcNow - ProcessStart;
}
