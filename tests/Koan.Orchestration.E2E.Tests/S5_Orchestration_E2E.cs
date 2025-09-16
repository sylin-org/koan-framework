using FluentAssertions;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Koan.Orchestration.E2E.Tests;

public class S5_Orchestration_E2E
{
    [Fact]
    public void Export_Compose_For_S5_Should_Succeed()
    {
        // Arrange: create a temporary descriptor for S5 infra (mongo, weaviate, ollama)
        var repoRoot = GetRepoRootFromSource();
        var composeOut = Path.Combine(repoRoot, ".Koan", "compose.yml");
        if (File.Exists(composeOut)) File.Delete(composeOut);

        var json = """
        {
            "services": [
                { "id": "mongo", "image": "mongo:7", "ports": ["5081:27017"], "volumes": ["./Data/mongo:/data/db"] },
                { "id": "weaviate", "image": "semitechnologies/weaviate:1.25.6", "env": { "QUERY_DEFAULTS_LIMIT": "25", "AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED": "true", "PERSISTENCE_DATA_PATH": "/var/lib/weaviate", "DEFAULT_VECTORIZER_MODULE": "none", "CLUSTER_HOSTNAME": "node1", "RAFT_BOOTSTRAP_EXPECT": "1" }, "ports": ["5082:8080"], "volumes": ["./Data/weaviate:/var/lib/weaviate"] },
                { "id": "ollama", "image": "ollama/ollama:latest", "ports": ["5083:11434"], "volumes": ["./Data/ollama:/root/.ollama"] }
            ]
        }
        """;
        using var desc = WithDescriptorJson(repoRoot, json);

        // Act: run `dotnet run --project src/Koan.Orchestration.Cli -- export compose --profile Local`
        var (code, _, stderr) = RunCli(repoRoot, "export compose --profile Local");

        // Assert
        code.Should().Be(0, $"CLI should export compose. stderr: {stderr}");
        File.Exists(composeOut).Should().BeTrue("compose.yml should be generated");

        var yaml = File.ReadAllText(composeOut);
        // Validate meaningful aspects: expected services and ports
        yaml.Should().Contain("services:");
        yaml.Should().Contain("mongo:");
        yaml.Should().Contain("weaviate:");
        yaml.Should().Contain("ollama:");
        yaml.Should().Contain("5081:27017");
        yaml.Should().Contain("5082:8080");
        yaml.Should().Contain("5083:11434");
        // Validate volume binds and env passthrough
        yaml.Should().Contain("./Data/mongo:/data/db");
        yaml.Should().Contain("./Data/weaviate:/var/lib/weaviate");
        yaml.Should().Contain("./Data/ollama:/root/.ollama");
        (yaml.Contains("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: \"true\"") || yaml.Contains("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: true"))
            .Should().BeTrue("env passthrough should be present");
    }

    [Fact]
    public async Task Up_And_Down_Should_Work_When_Enabled()
    {
        // Gate running containers in CI or dev machines. Opt-in via Koan_E2E_RUN_CONTAINERS=true
        var enabled = Environment.GetEnvironmentVariable("Koan_E2E_RUN_CONTAINERS");
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            return; // skip without failing

        var repoRoot = GetRepoRootFromSource();
        var composeOut = Path.Combine(repoRoot, ".Koan", "compose.yml");
        if (File.Exists(composeOut)) File.Delete(composeOut);
        var json = """
        {
            "services": [
                { "id": "mongo", "image": "mongo:7", "ports": ["5081:27017"] },
        { "id": "weaviate", "image": "semitechnologies/weaviate:1.25.6", "env": { "QUERY_DEFAULTS_LIMIT": "25", "AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED": "true", "PERSISTENCE_DATA_PATH": "/var/lib/weaviate", "DEFAULT_VECTORIZER_MODULE": "none", "CLUSTER_HOSTNAME": "node1", "RAFT_BOOTSTRAP_EXPECT": "1" }, "ports": ["5082:8080"] },
                { "id": "ollama", "image": "ollama/ollama:latest", "ports": ["5083:11434"] }
            ]
        }
        """;
        using var desc = WithDescriptorJson(repoRoot, json);
        var (codeExp, _, stderrExp) = RunCli(repoRoot, "export compose --profile Local");
        codeExp.Should().Be(0, $"export should succeed before up. stderr: {stderrExp}");

        // Proactive cleanup: ensure ports 5081-5084 are free from previous runs
        foreach (var port in new[] { 5081, 5082, 5083, 5084 })
        {
            if (IsPortOpen(port))
            {
                // Try CLI down first, then force-stop containers publishing the port
                _ = RunCli(repoRoot, "down --prune-data");
                TryStopContainersPublishingPort(port);
            }
        }

        // Start detached with a short readiness timeout to avoid long hangs
        var (codeUp, _, stderrUp) = RunCli(repoRoot, "up --profile Local --timeout 300");
        // Treat readiness timeout (4) as acceptable: we'll probe ports ourselves below
        codeUp.Should().BeOneOf(new[] { 0, 4 }, $"up should start stack or time out waiting on readiness. stderr: {stderrUp}");

        // Wait for DBs to be reachable; API may not be part of infra descriptor here
        await WaitUntil(() => CanConnectTcpAsync("127.0.0.1", 5081, TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(150)); // Mongo
        await WaitUntil(() => CanConnectTcpAsync("127.0.0.1", 5082, TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(150)); // Weaviate

        // Weaviate minimal HTTP check
        var codeWeaviate = await GetStatusCodeRawWithRetryAsync("127.0.0.1", 5082, "/v1/", attempts: 20);
        ((int)codeWeaviate).Should().BeInRange(200, 399, "weaviate should respond on /v1/");

        // Query status to ensure provider responds
        var (codeStatus, statusOut, _) = RunCli(repoRoot, "status");
        codeStatus.Should().Be(0);
        statusOut.Should().Contain("provider:");

        // Tear down and prune data to leave a clean machine
        var (codeDown, _, stderrDown) = RunCli(repoRoot, "down --prune-data");
        codeDown.Should().Be(0, $"down should prune data. stderr: {stderrDown}");
    }

    private static IDisposable WithDescriptorJson(string repoRoot, string json)
    {
        // Writes repo-root/Koan.orchestration.json and cleans it up on dispose
        var path = Path.Combine(repoRoot, "Koan.orchestration.json");
        File.WriteAllText(path, json);
        return new DelegateDisposable(() => { try { File.Delete(path); } catch { } });
    }

    private static (int Code, string StdOut, string StdErr) RunCli(string repoRoot, string args)
    {
        // Run as `dotnet run --project src/Koan.Orchestration.Cli -- <args>`
        var project = Path.Combine(repoRoot, "src", "Koan.Orchestration.Cli", "Koan.Orchestration.Cli.csproj");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --project \"{project}\" -- {args}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Stabilize port probing to avoid long scans
        psi.Environment["Koan_PORT_PROBE_MAX"] = "50";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static async Task WaitUntil(Func<Task<bool>> predicate, TimeSpan timeout, TimeSpan? poll = null)
    {
        poll ??= TimeSpan.FromSeconds(2);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await predicate()) return;
            await Task.Delay(poll.Value);
        }
        throw new TimeoutException($"Condition not met within {timeout.TotalSeconds:n0}s");
    }

    private static async Task<bool> CanConnectTcpAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch { return false; }
    }

    private static async Task<HttpStatusCode> GetStatusCodeRawWithRetryAsync(string host, int port, string path, int attempts = 10, TimeSpan? delay = null)
    {
        delay ??= TimeSpan.FromMilliseconds(300);
        Exception? last = null;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var code = await GetStatusCodeRawAsync(host, port, path, TimeSpan.FromSeconds(8));
                return code;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay.Value.TotalMilliseconds * Math.Pow(1.6, i), 1500)));
            }
        }
        throw last!;
    }

    private static async Task<HttpStatusCode> GetStatusCodeRawAsync(string host, int port, string path, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cts.Token);
        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

        await writer.WriteAsync($"GET {path} HTTP/1.1\r\n");
        await writer.WriteAsync($"Host: {host}\r\n");
        await writer.WriteAsync("Connection: close\r\n\r\n");
        await writer.FlushAsync();

        var statusLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(statusLine)) throw new IOException("Empty HTTP response");
        var parts = statusLine.Split(' ');
        if (parts.Length < 3 || !int.TryParse(parts[1], out var code))
            throw new IOException("Invalid HTTP status line: " + statusLine);
        return (HttpStatusCode)code;
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("127.0.0.1", port);
            var completed = task.Wait(TimeSpan.FromMilliseconds(300));
            return completed && client.Connected;
        }
        catch { return false; }
    }

    private static void TryStopContainersPublishingPort(int port)
    {
        var engine = DetectContainerEngine();
        if (engine == null) return;
        var ps = RunShell($"{engine} ps --no-trunc --format '{{{{.ID}}}}|{{{{.Ports}}}}|{{{{.Names}}}}'");
        if (ps.ExitCode != 0) return;
        var lines = ps.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            var id = parts[0].Trim();
            var ports = parts[1];
            if (ports.Contains($":{port}->")) ids.Add(id);
        }
        foreach (var id in ids.Distinct())
        {
            _ = RunShell($"{engine} rm -f {id}");
        }
    }

    private static string? DetectContainerEngine()
    {
        if (RunShell("docker --version").ExitCode == 0) return "docker";
        if (RunShell("podman --version").ExitCode == 0) return "podman";
        return null;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunShell(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pwsh.exe" : "/bin/bash",
            Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\""
                : $"-lc \"{command}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static string GetRepoRootFromSource([CallerFilePath] string sourceFile = "")
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
            throw new InvalidOperationException("CallerFilePath not available");
        // tests/<Project>/<file> -> repo root is two levels up from test project folder
        var srcDir = Path.GetDirectoryName(sourceFile)!;
        return Path.GetFullPath(Path.Combine(srcDir, "..", ".."));
    }

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public DelegateDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
