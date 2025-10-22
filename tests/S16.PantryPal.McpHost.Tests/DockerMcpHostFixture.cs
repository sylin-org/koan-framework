using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Runtime.InteropServices;
using Xunit;
using System.Diagnostics;

namespace S16.PantryPal.McpHost.Tests;

/// <summary>
/// Spins up the S16 PantryPal MCP Host inside a disposable docker container to avoid
/// coupling to TestServer / preview testhost packaging issues. Leverages the existing sample Dockerfile.
/// Requires: docker CLI available on PATH. Skips all tests if docker is unavailable.
/// </summary>
public sealed class DockerMcpHostFixture : IAsyncLifetime
{
    private string? _containerId;
    private string _imageTag = "s16-mcphost-test:latest";
    private static readonly HttpClient SharedClient = new();
    public HttpClient Client => SharedClient;
    private int _hostPort = 5026; // Dockerfile exposes 8080; compose mapped earlier to 5026. We'll map 5026 -> 8080 here for parity.

    public async Task InitializeAsync()
    {
        if (!await DockerAvailable())
        {
            // Mark an environment flag so individual tests can detect and self-skip if desired.
            Environment.SetEnvironmentVariable("S16_MCPHOST_DOCKER_UNAVAILABLE", "1");
            return; // do not fail fixture init; tests can assert skip condition
        }

        await BuildImage();
        _containerId = await RunContainer();
        await WaitForReady();
        SharedClient.BaseAddress = new Uri($"http://localhost:{_hostPort}");
    }

    public async Task DisposeAsync()
    {
        if (_containerId != null)
        {
            await ExecSilent($"docker rm -f {_containerId}");
        }
    }

    private static async Task<bool> DockerAvailable()
    {
        try
        {
            var (code, _) = await Exec("docker info --format '{{json .ServerVersion}}'");
            return code == 0;
        }
        catch { return false; }
    }

    private async Task BuildImage()
    {
        // Build context: samples/S16.PantryPal.McpHost
        var root = FindRepoRoot();
        var projPath = Path.Combine(root, "samples", "S16.PantryPal.McpHost");
        var (code, output) = await Exec($"docker build -t {_imageTag} .", projPath);
        if (code != 0)
            throw new InvalidOperationException($"Failed to build test image: {output}");
    }

    private async Task<string> RunContainer()
    {
        var (code, output) = await Exec($"docker run -d -p {_hostPort}:8080 {_imageTag}");
        if (code != 0 || string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException($"Failed to run container: {output}");
        return output.Trim();
    }

    private async Task WaitForReady()
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var resp = await SharedClient.GetAsync($"http://localhost:{_hostPort}/mcp/sdk/definitions", cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    var text = await resp.Content.ReadAsStringAsync();
                    if (text.Contains("integrity-sha256")) return; // ready
                }
            }
            catch { /* ignore until deadline */ }
            await Task.Delay(750);
        }
        throw new TimeoutException("Timed out waiting for MCP host readiness in container");
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Koan.sln"))) return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (Koan.sln not found)");
    }

    private static async Task<(int code, string output)> Exec(string cmd, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/sh",
            Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"/c {cmd}" : $"-c \"{cmd}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory()
        };
        var p = Process.Start(psi)!;
        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        return (p.ExitCode, sb.ToString());
    }

    private static async Task ExecSilent(string cmd) => await Exec(cmd);
}