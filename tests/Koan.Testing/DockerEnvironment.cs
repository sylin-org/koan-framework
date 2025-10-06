using Docker.DotNet;
using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Koan.Testing;

public static class DockerEnvironment
{
    private const int MaxLogPayloadLength = 512;

    public sealed record ProbeResult(bool Available, string? Endpoint, string? Message = null);

    public static async Task<ProbeResult> ProbeAsync()
    {
        // Honor explicit DOCKER_HOST first
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            Log($"Probe: DOCKER_HOST={dockerHost}");
            if (await PingAsync(dockerHost!))
            {
                Log("Probe: DOCKER_HOST ping succeeded.");
                return new(true, dockerHost);
            }

            Log("Probe: DOCKER_HOST ping failed.");
            return new(false, dockerHost, "DOCKER_HOST set but daemon not reachable");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pipeCandidates = new[]
            {
                "npipe://./pipe/docker_engine",
                "npipe:////./pipe/docker_engine",
                "npipe://./pipe/dockerDesktopLinuxEngine",
                "npipe:////./pipe/dockerDesktopLinuxEngine",
                "npipe://./pipe/dockerDesktopWindowsEngine",
                "npipe:////./pipe/dockerDesktopWindowsEngine"
            };

            foreach (var pipe in pipeCandidates)
            {
                Log($"Probe: Trying named pipe {pipe}");
                if (await PingAsync(pipe))
                {
                    Log($"Probe: Named pipe {pipe} succeeded.");
                    return new(true, pipe);
                }
            }

            var tcp = "http://localhost:2375";
            Log($"Probe: Trying TCP {tcp}");
            if (await PingAsync(tcp)) return new(true, tcp);
        }
        else
        {
            var unix = "unix:///var/run/docker.sock";
            Log($"Probe: Trying unix socket {unix}");
            if (await PingAsync(unix)) return new(true, unix);
            var tcp = "http://localhost:2375";
            Log($"Probe: Trying TCP {tcp}");
            if (await PingAsync(tcp)) return new(true, tcp);
        }

        var cliEndpoint = await TryGetDockerEndpointFromCliAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(cliEndpoint))
        {
            cliEndpoint = NormalizeEndpoint(cliEndpoint!);
            Log($"Probe: CLI endpoint candidate {cliEndpoint}");

            var version = await TryGetDockerVersionAsync().ConfigureAwait(false);
            var message = string.IsNullOrWhiteSpace(version)
                ? "Derived from docker CLI context"
                : $"CLI ok: {version}";

            if (await PingAsync(cliEndpoint).ConfigureAwait(false))
            {
                Log($"Probe: CLI detection succeeded. {message}");
                return new(true, cliEndpoint, message);
            }

            Log("Probe: CLI endpoint ping failed after detection.");
        }

        Log("Probe: No Docker endpoint detected.");
        return new(false, null, "No reachable Docker endpoint found");
    }

    public static DockerClient CreateClient(string endpoint)
    {
        var cfg = new DockerClientConfiguration(new Uri(endpoint));
        return cfg.CreateClient();
    }

    private static async Task<bool> PingAsync(string endpoint)
    {
        try
        {
            using var client = CreateClient(endpoint);
            await client.System.PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log($"Probe: Ping to {endpoint} failed - {ex.Message}");
            return false;
        }
    }

    private static async Task<string?> TryGetDockerEndpointFromCliAsync()
    {
        var (ctxOk, ctxOutput) = await TryRunDockerCliAsync("context show").ConfigureAwait(false);
        if (!ctxOk)
        {
            Log($"Probe: docker context show failed. {Truncate(ctxOutput)}");
        }
        var contextName = ctxOk ? ctxOutput?.Trim() : null;
        if (string.IsNullOrWhiteSpace(contextName))
        {
            contextName = "default";
        }

        var inspectArgs = $"context inspect {contextName}";
        var (inspectOk, inspectOutput) = await TryRunDockerCliAsync(inspectArgs).ConfigureAwait(false);
        if (!inspectOk || string.IsNullOrWhiteSpace(inspectOutput))
        {
            if (!inspectOk)
            {
                Log($"Probe: docker {inspectArgs} failed. {Truncate(inspectOutput)}");
            }
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(inspectOutput);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                if (first.TryGetProperty("Endpoints", out var endpoints) &&
                    endpoints.TryGetProperty("docker", out var docker) &&
                    docker.TryGetProperty("Host", out var hostProp))
                {
                    var endpoint = hostProp.GetString();
                    return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Probe: Failed to parse docker context inspect output - {ex.Message}");
            // ignore parse errors and fall through to null
        }

        return null;
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && endpoint.StartsWith("npipe:", StringComparison.OrdinalIgnoreCase))
        {
            // docker context inspect returns npipe:////./pipe/...; normalize to npipe://./pipe/...
            endpoint = endpoint.Replace("////./", "//./", StringComparison.OrdinalIgnoreCase);

            // Just in case there are triple slashes after normalization
            while (endpoint.Contains("///"))
            {
                endpoint = endpoint.Replace("///", "//");
            }
        }

        return endpoint;
    }

    private static async Task<string?> TryGetDockerVersionAsync()
    {
        var attempts = new[]
        {
            "version --format \"{{.Server.Version}}\"",
            "info --format \"{{.ServerVersion}}\"",
            "version"
        };

        foreach (var args in attempts)
        {
            var (ok, output) = await TryRunDockerCliAsync(args).ConfigureAwait(false);
            if (!ok || string.IsNullOrWhiteSpace(output))
            {
                continue;
            }

            var ver = output.Trim('"', '\'', '\r', '\n', ' ');
            if (!string.IsNullOrWhiteSpace(ver))
            {
                return ver;
            }
        }

        return null;
    }

    private static void Log(string message)
    {
        try
        {
            Console.Error.WriteLine($"[DockerEnvironment] {message}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static async Task<(bool ok, string? output)> TryRunDockerCliAsync(string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "docker.exe" : "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                Log($"Probe: Failed to start docker process for '{arguments}'.");
                return (false, null);
            }

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var merged = string.IsNullOrWhiteSpace(error) ? output : error;
                Log($"Probe: docker {arguments} exited with code {process.ExitCode}. {Truncate(merged)}");
                return (false, merged);
            }

            return (true, string.IsNullOrWhiteSpace(output) ? error : output);
        }
        catch (Exception ex)
        {
            Log($"Probe: Exception running docker {arguments} - {ex.Message}");
            return (false, null);
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var truncated = value!;
        return truncated.Length <= MaxLogPayloadLength
            ? truncated
            : truncated[..MaxLogPayloadLength] + "…";
    }
}
