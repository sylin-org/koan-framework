using System.Diagnostics;
using System.Text;
using Koan.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Manages Docker containers for dependency orchestration
/// </summary>
public class DockerContainerManager : IKoanContainerManager
{
    private readonly ILogger<DockerContainerManager> _logger;

    public DockerContainerManager(ILogger<DockerContainerManager> logger)
    {
        _logger = logger;
    }

    public async Task<string> StartContainerAsync(DependencyDescriptor dependency, string appInstance, string sessionId, CancellationToken cancellationToken = default)
    {
        var containerName = $"{dependency.Name}-{appInstance}";

        // Check if container already exists and is running
        if (await IsContainerRunningAsync(containerName, cancellationToken))
        {
            _logger.LogInformation("Container {ContainerName} already running", containerName);
            return containerName;
        }

        // Stop and remove any existing container with the same name
        await StopContainerAsync(containerName, cancellationToken);

        var dockerCommand = BuildDockerRunCommand(dependency, containerName, appInstance, sessionId);
        _logger.LogDebug("Starting container with command: {Command}", dockerCommand);

        var result = await ExecuteDockerCommandAsync(dockerCommand, cancellationToken);
        if (result.ExitCode != 0)
        {
            _logger.LogError("Failed to start container {ContainerName}. Error: {Error}", containerName, result.Error);
            throw new InvalidOperationException($"Failed to start container {containerName}. Docker error: {result.Error}");
        }

        _logger.LogInformation("Started container: {ContainerName}", containerName);
        return containerName;
    }

    public async Task StopContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            // First try to stop gracefully
            var stopResult = await ExecuteDockerCommandAsync($"stop {containerName}", cancellationToken);
            if (stopResult.ExitCode == 0)
            {
                _logger.LogDebug("Stopped container: {ContainerName}", containerName);
            }

            // Then remove the container
            var removeResult = await ExecuteDockerCommandAsync($"rm {containerName}", cancellationToken);
            if (removeResult.ExitCode == 0)
            {
                _logger.LogDebug("Removed container: {ContainerName}", containerName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop/remove container {ContainerName}", containerName);
        }
    }

    public async Task<bool> IsContainerRunningAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ExecuteDockerCommandAsync($"ps -q --filter name={containerName}", cancellationToken);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ExecuteDockerCommandAsync("version", cancellationToken);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> WaitForContainerHealthyAsync(string containerName, DependencyDescriptor dependency, CancellationToken cancellationToken = default)
    {
        var timeout = dependency.HealthTimeout;
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (!string.IsNullOrEmpty(dependency.HealthCheckCommand))
            {
                var healthResult = await ExecuteDockerCommandAsync($"exec {containerName} {dependency.HealthCheckCommand}", cancellationToken);
                if (healthResult.ExitCode == 0)
                {
                    _logger.LogDebug("Container {ContainerName} is healthy", containerName);
                    return true;
                }
            }
            else
            {
                // Simple check - is the container still running?
                if (await IsContainerRunningAsync(containerName, cancellationToken))
                {
                    // Wait a bit for the service to start inside the container
                    await Task.Delay(1000, cancellationToken);
                    _logger.LogDebug("Container {ContainerName} is running (no specific health check)", containerName);
                    return true;
                }
            }

            await Task.Delay(1000, cancellationToken);
        }

        _logger.LogWarning("Container {ContainerName} did not become healthy within {Timeout}", containerName, timeout);
        return false;
    }

    public async Task CleanupSessionContainersAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all containers with the session label
            var containersResult = await ExecuteDockerCommandAsync($"ps -aq --filter label=koan.session={sessionId}", cancellationToken);
            if (containersResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(containersResult.Output))
            {
                var containerIds = containersResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var containerId in containerIds)
                {
                    await ExecuteDockerCommandAsync($"stop {containerId}", cancellationToken);
                    await ExecuteDockerCommandAsync($"rm {containerId}", cancellationToken);
                }
                _logger.LogInformation("Cleaned up {Count} containers for session {SessionId}", containerIds.Length, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup containers for session {SessionId}", sessionId);
        }
    }

    public async Task CleanupOrphanedKoanContainersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all containers with Koan auto-cleanup label
            var containersResult = await ExecuteDockerCommandAsync("ps -aq --filter label=koan.auto-cleanup=true", cancellationToken);
            if (containersResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(containersResult.Output))
            {
                var containerIds = containersResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var cleanedCount = 0;

                foreach (var containerId in containerIds)
                {
                    // Get container details to determine if it's orphaned
                    var inspectResult = await ExecuteDockerCommandAsync($"inspect {containerId} --format '{{{{.State.Status}}}} {{{{index .Config.Labels \"koan.session\"}}}} {{{{index .Config.Labels \"koan.created\"}}}}'", cancellationToken);

                    if (inspectResult.ExitCode == 0)
                    {
                        var parts = inspectResult.Output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            var status = parts[0];
                            var sessionId = parts[1];
                            var createdTime = parts[2];

                            // Consider container orphaned if it's been running for more than 1 hour
                            // (indicates previous session didn't clean up properly)
                            if (DateTime.TryParse(createdTime, out var created) &&
                                DateTime.UtcNow - created > TimeSpan.FromHours(1))
                            {
                                _logger.LogWarning("Cleaning up orphaned Koan container {ContainerId} from session {SessionId} created {Created}",
                                    containerId, sessionId, created);

                                await ExecuteDockerCommandAsync($"stop {containerId}", cancellationToken);
                                await ExecuteDockerCommandAsync($"rm {containerId}", cancellationToken);
                                cleanedCount++;
                            }
                        }
                    }
                }

                if (cleanedCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} orphaned Koan containers", cleanedCount);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup orphaned Koan containers");
        }
    }

    public async Task CleanupAppInstanceContainersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentAppInstance = Environment.GetEnvironmentVariable("KOAN_APP_INSTANCE");
            var currentSessionId = Environment.GetEnvironmentVariable("KOAN_APP_SID");

            if (string.IsNullOrEmpty(currentAppInstance) || string.IsNullOrEmpty(currentSessionId))
            {
                _logger.LogDebug("App instance or session ID not available, skipping app-specific cleanup");
                return;
            }

            // Get all containers for this app instance but different sessions (crashed instances)
            var containersResult = await ExecuteDockerCommandAsync($"ps -aq --filter label=koan.app-instance={currentAppInstance}", cancellationToken);
            if (containersResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(containersResult.Output))
            {
                var containerIds = containersResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var cleanedCount = 0;

                foreach (var containerId in containerIds)
                {
                    // Get container session ID to check if it's from a different session
                    var inspectResult = await ExecuteDockerCommandAsync($"inspect {containerId} --format '{{{{index .Config.Labels \"koan.session\"}}}}'", cancellationToken);

                    if (inspectResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(inspectResult.Output))
                    {
                        var containerSessionId = inspectResult.Output.Trim();

                        // Clean up containers from different sessions (crashed app instances)
                        if (containerSessionId != currentSessionId)
                        {
                            _logger.LogInformation("Cleaning up container {ContainerId} from crashed app instance (session {SessionId})",
                                containerId, containerSessionId);

                            await ExecuteDockerCommandAsync($"stop {containerId}", cancellationToken);
                            await ExecuteDockerCommandAsync($"rm {containerId}", cancellationToken);
                            cleanedCount++;
                        }
                    }
                }

                if (cleanedCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} containers from crashed app instances", cleanedCount);
                }
                else
                {
                    _logger.LogDebug("No crashed app instance containers found to clean up");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup app instance containers");
        }
    }

    private string BuildDockerRunCommand(DependencyDescriptor dependency, string containerName, string appInstance, string sessionId)
    {
        var cmd = new StringBuilder();
        cmd.Append($"run -d --name {containerName}");

        // Add port mapping
        cmd.Append($" -p {dependency.Port}:{dependency.Port}");

        // Add additional ports if specified
        if (dependency.Ports != null)
        {
            foreach (var (externalPort, internalPort) in dependency.Ports)
            {
                cmd.Append($" -p {externalPort}:{internalPort}");
            }
        }

        // Add labels for tracking and cleanup (enhanced with app identity)
        cmd.Append($" --label koan.session={sessionId}");
        cmd.Append($" --label koan.auto-cleanup=true");
        cmd.Append($" --label koan.dependency={dependency.Name}");
        cmd.Append($" --label koan.created={DateTimeOffset.UtcNow:O}");

        // Add app identity labels for crash recovery
        var appId = Environment.GetEnvironmentVariable("KOAN_APP_ID") ?? "UnknownApp";
        cmd.Append($" --label koan.app-id={appId}");
        cmd.Append($" --label koan.app-instance={appInstance}");

        // Add environment variables
        foreach (var env in dependency.Environment)
        {
            cmd.Append($" -e \"{env.Key}={env.Value}\"");
        }

        // Add volumes
        foreach (var volume in dependency.Volumes)
        {
            cmd.Append($" -v {volume}");
        }

        // Add the image
        cmd.Append($" {dependency.Image}");

        return cmd.ToString();
    }

    private async Task<DockerCommandResult> ExecuteDockerCommandAsync(string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            return new DockerCommandResult
            {
                ExitCode = process.ExitCode,
                Output = output?.Trim() ?? "",
                Error = error?.Trim() ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute docker command: {Arguments}", arguments);
            return new DockerCommandResult
            {
                ExitCode = -1,
                Output = "",
                Error = ex.Message
            };
        }
    }

    private class DockerCommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
    }
}