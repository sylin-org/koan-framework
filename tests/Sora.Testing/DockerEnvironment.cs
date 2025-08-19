using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Docker.DotNet;

namespace Sora.Testing;

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
}
