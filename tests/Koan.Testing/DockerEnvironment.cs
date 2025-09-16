using Docker.DotNet;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Koan.Testing;

public static class DockerEnvironment
{
    public sealed record ProbeResult(bool Available, string? Endpoint, string? Message = null);

    public static async Task<ProbeResult> ProbeAsync()
    {
        // Honor explicit DOCKER_HOST first
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            if (await PingAsync(dockerHost!)) return new(true, dockerHost);
            return new(false, dockerHost, "DOCKER_HOST set but daemon not reachable");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var npipe = "npipe://./pipe/docker_engine";
            if (await PingAsync(npipe)) return new(true, npipe);
            var tcp = "http://localhost:2375";
            if (await PingAsync(tcp)) return new(true, tcp);
        }
        else
        {
            var unix = "unix:///var/run/docker.sock";
            if (await PingAsync(unix)) return new(true, unix);
            var tcp = "http://localhost:2375";
            if (await PingAsync(tcp)) return new(true, tcp);
        }

        // As a last resort, if docker CLI is present and returns a server version, assume default endpoint per OS
        try
        {
            var (ok, ver) = await TryDockerCliVersionAsync();
            if (ok)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return new(true, "npipe://./pipe/docker_engine", $"CLI ok: {ver}");
                else
                    return new(true, "unix:///var/run/docker.sock", $"CLI ok: {ver}");
            }
        }
        catch { }

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
        catch
        {
            return false;
        }
    }

    private static async Task<(bool ok, string? version)> TryDockerCliVersionAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "docker.exe" : "docker",
                Arguments = "version --format '{{.Server.Version}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var ver = output?.Trim();
            return (!string.IsNullOrWhiteSpace(ver), ver);
        }
        catch { return (false, null); }
    }
}
