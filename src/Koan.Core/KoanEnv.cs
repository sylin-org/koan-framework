using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.Bootstrap;
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
            logger.LogInformation("Environment snapshot:");
            logger.LogInformation("EnvironmentName: {EnvironmentName}", snap.EnvironmentName);
            logger.LogInformation("IsDevelopment: {IsDevelopment}", snap.IsDevelopment);
            logger.LogInformation("IsProduction: {IsProduction}", snap.IsProduction);
            logger.LogInformation("IsStaging: {IsStaging}", snap.IsStaging);
            logger.LogInformation("InContainer: {InContainer}", snap.InContainer);
            logger.LogInformation("IsCi: {IsCi}", snap.IsCi);
            logger.LogInformation("AllowMagicInProduction: {AllowMagicInProduction}", snap.AllowMagicInProduction);
            logger.LogInformation("ProcessStart: {ProcessStart:O}", snap.ProcessStart);
            logger.LogInformation("OrchestrationMode: {OrchestrationMode}", snap.OrchestrationMode);
            logger.LogInformation("SessionId: {SessionId}", snap.SessionId);
            logger.LogInformation("KoanAssemblies: {KoanAssemblyCount} loaded", KoanAssemblies.Length);
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
            Console.WriteLine($"  OrchestrationMode: {snap.OrchestrationMode}");
            Console.WriteLine($"  SessionId: {snap.SessionId}");
            Console.WriteLine($"  KoanAssemblies: {KoanAssemblies.Length} loaded");
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

    private static OrchestrationMode DetectOrchestrationMode(IConfiguration? cfg, bool isDevelopment, bool inContainer)
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

        // Priority 5: Self-orchestration (development host spawning containers)
        if (ShouldSelfOrchestrate(cfg, isDevelopment, inContainer))
        {
            return OrchestrationMode.SelfOrchestrating;
        }

        // Default: Standalone mode (production with external dependencies)
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

    private static bool ShouldSelfOrchestrate(IConfiguration? cfg, bool isDevelopment, bool inContainer)
    {
        // Don't self-orchestrate if already in a container
        if (inContainer) return false;

        // Don't self-orchestrate in production unless explicitly enabled
        if (!isDevelopment && !Configuration.Read(cfg, Infrastructure.Constants.Configuration.Orchestration.EnableSelfOrchestration, false))
        {
            return false;
        }

        // In development, self-orchestrate if explicitly enabled or if it's the best option
        return isDevelopment || Configuration.Read(cfg, Infrastructure.Constants.Configuration.Orchestration.EnableSelfOrchestration, false);
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
        var orchestrationMode = DetectOrchestrationMode(cfg, isDev, inContainer);
        var sessionId = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Env.KoanSessionId, null) ??
                       Guid.NewGuid().ToString("N")[..8];

        return new Snapshot(envName, isDev, isProd, isStg, inContainer, isCi, magic, DateTimeOffset.UtcNow, orchestrationMode, sessionId);
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
    public static OrchestrationMode OrchestrationMode => Current.OrchestrationMode;
    public static string SessionId => Current.SessionId;
    public static Assembly[] KoanAssemblies => AssemblyCache.Instance.GetKoanAssemblies();

    private sealed record Snapshot(
        string EnvironmentName,
        bool IsDevelopment,
        bool IsProduction,
        bool IsStaging,
        bool InContainer,
        bool IsCi,
        bool AllowMagicInProduction,
        DateTimeOffset ProcessStart,
        OrchestrationMode OrchestrationMode,
        string SessionId
    );
}
