using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Koan.Core;
using Koan.Core.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Orchestrates dependencies for self-hosted Koan applications by discovering
/// required dependencies from loaded assemblies and managing their lifecycle
/// </summary>
public class KoanDependencyOrchestrator : IKoanDependencyOrchestrator
{
    private readonly IKoanContainerManager _containerManager;
    private readonly ILogger<KoanDependencyOrchestrator> _logger;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IKoanOrchestrationEvaluator> _orchestrationEvaluators;
    private readonly List<DependencyDescriptor> _managedDependencies = new();
    private readonly string _sessionId;
    private readonly string _appId;
    private readonly string _appInstance;
    private readonly Dictionary<string, string> _koanEnvironmentVariables;

    public KoanDependencyOrchestrator(
        IKoanContainerManager containerManager,
        ILogger<KoanDependencyOrchestrator> logger,
        IConfiguration configuration,
        IEnumerable<IKoanOrchestrationEvaluator> orchestrationEvaluators)
    {
        _containerManager = containerManager;
        _logger = logger;
        _configuration = configuration;
        _orchestrationEvaluators = orchestrationEvaluators;

        // Generate app identity using KISS approach
        _appId = Assembly.GetEntryAssembly()?.GetName().Name ?? "UnknownApp";
        _appInstance = GenerateAppInstance(_appId);
        _sessionId = KoanEnv.SessionId;

        // Set environment variables for container labeling and other components
        Environment.SetEnvironmentVariable("KOAN_APP_ID", _appId);
        Environment.SetEnvironmentVariable("KOAN_APP_INSTANCE", _appInstance);
        Environment.SetEnvironmentVariable("KOAN_APP_SID", _sessionId);
        Environment.SetEnvironmentVariable("KOAN_SESSION_ID", _sessionId); // Maintain backward compatibility

        // Initialize cached Koan environment variables for container injection
        _koanEnvironmentVariables = new Dictionary<string, string>
        {
            ["KOAN_SESSION_ID"] = _sessionId,
            ["KOAN_APP_SID"] = _sessionId,
            ["KOAN_APP_ID"] = _appId,
            ["KOAN_APP_INSTANCE"] = _appInstance,
            ["KOAN_MANAGED_BY"] = "self-orchestration"
        };
    }

    public async Task<List<string>> StartDependenciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Self-orchestration starting dependency discovery for session {SessionId}", _sessionId);

        // Discover dependencies using orchestration evaluators
        var dependencies = await DiscoverRequiredDependenciesAsync(cancellationToken);

        if (dependencies.Count == 0)
        {
            _logger.LogInformation("No dependencies discovered - continuing without orchestration");
            return new List<string>();
        }

        _logger.LogInformation("Self-orchestration starting {Count} dependencies: {Dependencies}",
            dependencies.Count, string.Join(", ", dependencies.Select(d => d.Name)));

        // Start dependencies in priority order
        var startedContainers = new List<string>();
        foreach (var dependency in dependencies.OrderBy(d => d.StartupPriority))
        {
            try
            {
                var containerName = await _containerManager.StartContainerAsync(dependency, _appInstance, _sessionId, cancellationToken);
                _managedDependencies.Add(dependency);
                startedContainers.Add(containerName);

                _logger.LogDebug("Waiting for {DependencyName} to become healthy...", dependency.Name);
                var isHealthy = await _containerManager.WaitForContainerHealthyAsync(containerName, dependency, cancellationToken);

                if (!isHealthy)
                {
                    _logger.LogWarning("{DependencyName} did not become healthy within timeout", dependency.Name);
                }
                else
                {
                    _logger.LogInformation("{DependencyName} is healthy and ready", dependency.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start dependency {DependencyName}", dependency.Name);
                throw new InvalidOperationException($"Failed to start required dependency {dependency.Name}: {ex.Message}", ex);
            }
        }

        _logger.LogInformation("All dependencies started successfully - application ready to start");
        return startedContainers;
    }

    public async Task StopDependenciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Self-orchestration stopping {Count} dependencies for session {SessionId}",
            _managedDependencies.Count, _sessionId);

        try
        {
            await _containerManager.CleanupSessionContainersAsync(_sessionId, cancellationToken);
            _managedDependencies.Clear();
            _logger.LogInformation("Self-orchestration cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop dependencies");
        }
    }

    public async Task<List<DependencyDescriptor>> GetManagedDependenciesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_managedDependencies.ToList());
    }

    private string GenerateAppInstance(string appId)
    {
        // Create unique instance ID: appId + path hash (KISS approach)
        var currentPath = Path.GetFileName(Environment.CurrentDirectory) ?? "unknown";
        var instanceInput = $"{appId}-{currentPath}";

        // Use SHA256 like other Koan components for consistency
        using var hasher = SHA256.Create();
        var hashBytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(instanceInput));
        var hashHex = Convert.ToHexString(hashBytes);

        // Return app-id + first 8 chars of hash (keeps it readable but unique)
        return $"{appId}-{hashHex[..8].ToLowerInvariant()}";
    }

    private async Task<List<DependencyDescriptor>> DiscoverRequiredDependenciesAsync(CancellationToken cancellationToken = default)
    {
        var dependencies = new List<DependencyDescriptor>();

        _logger.LogDebug("Discovering dependencies using {EvaluatorCount} orchestration evaluators", _orchestrationEvaluators.Count());

        // Create orchestration context
        var context = new OrchestrationContext
        {
            Mode = KoanEnv.OrchestrationMode,
            SessionId = _sessionId,
            AppId = _appId,
            AppInstance = _appInstance,
            EnvironmentVariables = _koanEnvironmentVariables
        };

        // Evaluate all services in parallel for performance
        var evaluationTasks = _orchestrationEvaluators.Select(async evaluator =>
        {
            try
            {
                _logger.LogDebug("Evaluating {ServiceName} orchestration requirements", evaluator.ServiceName);
                var decision = await evaluator.EvaluateAsync(_configuration, context);
                return new { Evaluator = evaluator, Decision = decision };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate {ServiceName} orchestration requirements", evaluator.ServiceName);
                var skipDecision = new OrchestrationDecision
                {
                    Action = OrchestrationAction.Skip,
                    Reason = $"Failed to evaluate: {ex.Message}"
                };
                return new { Evaluator = evaluator, Decision = skipDecision };
            }
        });

        var evaluationResults = await Task.WhenAll(evaluationTasks);

        // Process results and collect dependencies that need provisioning
        foreach (var result in evaluationResults)
        {
            if (result.Decision.Action == default)
            {
                _logger.LogWarning("Skipping {ServiceName} due to evaluation failure", result.Evaluator.ServiceName);
                continue;
            }

            var decision = result.Decision;
            _logger.LogInformation("[{ServiceName}] Decision: {Action} - {Reason}",
                result.Evaluator.ServiceName, decision.Action, decision.Reason);

            if (decision.Action == OrchestrationAction.ProvisionContainer && decision.DependencyDescriptor != null)
            {
                dependencies.Add(decision.DependencyDescriptor);
                _logger.LogDebug("Added {ServiceName} to provisioning queue", result.Evaluator.ServiceName);
            }
        }

        return dependencies;
    }

}