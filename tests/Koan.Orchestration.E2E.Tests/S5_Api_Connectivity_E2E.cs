using FluentAssertions;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Koan.Orchestration.E2E.Tests;

public class S5_Api_Connectivity_E2E
{
    // Host ports as defined by samples/S5.Recs/docker/compose.yml
    private const int ApiPort = 5084;
    private const int MongoPort = 5081;
    private const int WeaviatePort = 5082;
    private const int OllamaPort = 5083;

    [Fact]
    public async Task S5_Stack_Should_Expose_Endpoints_And_Databases_When_Enabled()
    {
        // Gate to avoid unintended container runs
        var enabled = Environment.GetEnvironmentVariable("Koan_E2E_RUN_CONTAINERS");
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            return;

        // Detect compose tool: docker compose > podman compose > podman-compose
        var composeTool = FindComposeTool();
        if (composeTool == null)
            return; // skip only when both Docker and Podman are missing

        var repoRoot = GetRepoRootFromSource();
        var composeFile = Path.Combine(repoRoot, "samples", "S5.Recs", "docker", "compose.yml");
        File.Exists(composeFile).Should().BeTrue($"compose file missing: {composeFile}");

        var composeDir = Path.GetDirectoryName(composeFile)!;
        var project = $"Koane2e-{Guid.NewGuid().ToString("N")[..8]}";

        // Initialization requirement: ensure previous runs are fully torn down (free ports 5081-5084)
        // Try teardown with the default compose project name (directory basename) and our isolated name.
        _ = RunShell(ComposeCmdDefaultProject(composeTool, composeDir, "down -v --remove-orphans"));
        _ = RunShell(ComposeCmd(composeTool, composeDir, "down -v --remove-orphans", project));

        // Additionally, free any lingering containers holding our known ports (from stray runs)
        EnsureS5PortsAreFree(composeTool);

        // Preflight: validate YAML with `compose config` for clearer errors
        Console.WriteLine($"compose tool: {composeTool}\nproject: {project}\ndir: {composeDir}");
        var cfg = RunShell(ComposeCmd(composeTool, composeDir, "config", project));
        Console.WriteLine("compose config (stdout):\n" + cfg.Stdout);
        cfg.ExitCode.Should().Be(0, $"compose config failed ({composeTool}). stderr: {cfg.Stderr}");

        // Up stack (build + detach)
        var up = RunShell(ComposeCmd(composeTool, composeDir, "up -d --build", project));
        up.ExitCode.Should().Be(0, $"compose up failed ({composeTool}). stderr: {up.Stderr}");
        // Quick visibility into what ports are published right after up
        var psAfterUp = RunShell(ComposeCmd(composeTool, composeDir, "ps --all", project));
        Console.WriteLine("compose ps --all (after up):\n" + psAfterUp.Stdout);
        // Wait until the api service is running per compose status to avoid probing too early
        try
        {
            await WaitUntil(() => Task.FromResult(IsServiceRunning(composeTool, composeDir, project, "api")), TimeSpan.FromSeconds(150));
        }
        catch (TimeoutException)
        {
            var psDiag = RunShell(ComposeCmd(composeTool, composeDir, "ps --all", project));
            Console.WriteLine("compose ps --all (on api wait timeout):\n" + psDiag.Stdout);
            var logsDiag = RunShell(ComposeCmd(composeTool, composeDir, "logs --no-color --tail=200", project));
            Console.WriteLine("compose logs (tail on api wait timeout):\n" + logsDiag.Stdout);
            throw;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var netstat = RunShell("netstat -ano -p tcp | Select-String ':5084' | ForEach-Object { $_.Line }");
            Console.WriteLine("netstat -ano -p tcp (filter :5084):\n" + netstat.Stdout);
        }

        try
        {
            var apiBase = new Uri($"http://127.0.0.1:{ApiPort}");
            using var http = CreateHttpClient(TimeSpan.FromSeconds(30));

            // Wait for API to be reachable: use raw TCP only (HTTP probes can be flaky early in startup)
            var tcpReady = false;
            try
            {
                await WaitUntil(async () =>
                {
                    return await CanConnectTcpAsync("127.0.0.1", ApiPort, TimeSpan.FromSeconds(2));
                }, TimeSpan.FromSeconds(150));
                tcpReady = true;
            }
            catch (TimeoutException)
            {
                // Dump diagnostics
                var ps = RunShell(ComposeCmd(composeTool, composeDir, "ps --all", project));
                Console.WriteLine("compose ps --all:\n" + ps.Stdout);
                var logs = RunShell(ComposeCmd(composeTool, composeDir, "logs --no-color --tail=200", project));
                Console.WriteLine("compose logs (tail):\n" + logs.Stdout);
                // Fallback: try HTTP liveness directly; if it works, proceed anyway.
                try
                {
                    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var probeResp = await probe.GetAsync($"http://127.0.0.1:{ApiPort}/health/live");
                    if (probeResp.IsSuccessStatusCode)
                    {
                        Console.WriteLine("TCP readiness timed out, but HTTP /health/live succeeded — proceeding.");
                        tcpReady = true;
                    }
                }
                catch { /* ignore, we'll fail below if not recovered */ }
                if (!tcpReady) throw;
            }

            // Give the API a brief stabilization window after initial readiness
            await Task.Delay(1000);

            // 1) API endpoints (use raw HTTP over TCP for robustness)
            var sLive = await GetStatusCodeRawWithRetryAsync("127.0.0.1", ApiPort, "/health/live", attempts: 20);
            sLive.Should().Be(HttpStatusCode.OK, "/health/live should be reachable");
            var sReady = await GetStatusCodeRawWithRetryAsync("127.0.0.1", ApiPort, "/health/ready", attempts: 20);
            sReady.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

            var sCaps = await GetStatusCodeRawWithRetryAsync("127.0.0.1", ApiPort, "/.well-known/Koan/observability", attempts: 20);
            ((int)sCaps).Should().BeInRange(200, 299, "observability should be reachable");

            var sGenres = await GetStatusCodeRawWithRetryAsync("127.0.0.1", ApiPort, "/api/genres", attempts: 20);
            ((int)sGenres).Should().BeInRange(200, 299, "/api/genres should be reachable");

            // 2) Databases
            // - Mongo (TCP connect to 5081)
            var mongoOk = await CanConnectTcpAsync("127.0.0.1", MongoPort, TimeSpan.FromSeconds(5));
            mongoOk.Should().BeTrue("Mongo should accept TCP connections on 5081");

            // - Weaviate (HTTP 200 on /v1/)
            var sWeav = await GetStatusCodeRawWithRetryAsync("127.0.0.1", WeaviatePort, "/v1/", attempts: 20);
            ((int)sWeav).Should().BeInRange(200, 299, "Weaviate should be reachable on 5082");
        }
        finally
        {
            // Careful teardown: down + remove volumes
            var down = RunShell(ComposeCmd(composeTool, composeDir, "down -v --remove-orphans", project));
            if (down.ExitCode != 0)
                Console.WriteLine($"compose down failed: {down.Stderr}");
        }
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

    private static async Task<bool> CanHttpGetAsync(string url, TimeSpan timeout)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout };
            var resp = await http.GetAsync(url);
            return resp.IsSuccessStatusCode;
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
                var code = await GetStatusCodeRawAsync(host, port, path, TimeSpan.FromSeconds(5));
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

        // Read status line
        var statusLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(statusLine)) throw new IOException("Empty HTTP response");
        // Expected: HTTP/1.1 200 OK
        var parts = statusLine.Split(' ');
        if (parts.Length < 3 || !int.TryParse(parts[1], out var code))
            throw new IOException("Invalid HTTP status line: " + statusLine);
        return (HttpStatusCode)code;
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = true,
            UseProxy = false,
            MaxConnectionsPerServer = 50,
        };
        var client = new HttpClient(handler)
        {
            Timeout = timeout,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        return client;
    }

    private static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, Uri uri, int attempts = 5, TimeSpan? delay = null)
        => await GetWithRetryAsync(http, uri.ToString(), attempts, delay);

    private static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string url, int attempts = 5, TimeSpan? delay = null)
    {
        delay ??= TimeSpan.FromMilliseconds(300);
        Exception? last = null;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var get = new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = HttpVersion.Version11
                };
                get.Headers.ConnectionClose = true;
                get.Headers.Accept.ParseAdd("application/json");
                get.Headers.UserAgent.ParseAdd("KoanE2E/1.0");
                var resp = await http.SendAsync(get, HttpCompletionOption.ResponseContentRead, cts.Token);
                return resp;
            }
            catch (Exception ex) when (
                ex is HttpRequestException ||
                ex is IOException ||
                ex is TaskCanceledException ||
                ex is SocketException)
            {
                last = ex;
                var wait = TimeSpan.FromMilliseconds(Math.Min(delay.Value.TotalMilliseconds * Math.Pow(1.8, i), 2_000));
                await Task.Delay(wait);
            }
        }
        // On persistent failure, dump quick diagnostics to aid triage
        Console.WriteLine($"HTTP GET failed after {attempts} attempts: {url}\nError: {last}");
        throw last!;
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

    private static (int ExitCode, string Stdout, string Stderr) RunShell(string command)
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

    private static string? FindComposeTool()
    {
        // Prefer Docker (plugin) if present
        if (RunShell("docker --version").ExitCode == 0)
        {
            var plug = RunShell("docker compose version");
            if (plug.ExitCode == 0) return "docker compose";
            // Classic docker-compose
            if (RunShell("docker-compose --version").ExitCode == 0) return "docker-compose";
        }

        // Podman (native compose)
        if (RunShell("podman --version").ExitCode == 0)
        {
            var probe = RunShell("podman compose version");
            if (probe.ExitCode == 0) return "podman compose";
            if (RunShell("podman-compose --version").ExitCode == 0) return "podman-compose";
            return "podman compose";
        }

        // Stand-alone podman-compose as last resort
        if (RunShell("podman-compose --version").ExitCode == 0) return "podman-compose";
        return null;
    }

    private static bool IsServiceRunning(string tool, string projectDir, string projectName, string service)
    {
        // Prefer compose ps output and look for the api service row with a running/Up status
        var ps = RunShell(ComposeCmd(tool, projectDir, "ps --all", projectName));
        if (ps.ExitCode != 0) return false;
        var lines = ps.Stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1) // skip header
            .ToArray();
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains($" {service} ") || lower.Contains($"-{service}-"))
            {
                // Compose status usually includes words like "running", "up", "healthy"
                if (lower.Contains("running") || lower.Contains(" up ") || lower.Contains("healthy"))
                    return true;
            }
        }
        return false;
    }

    private static string ComposeCmd(string tool, string projectDir, string args, string projectName)
    {
        // Use project directory and relative compose file; set project name to isolate resources.
        // Windows (PowerShell): prefix env with $env:VAR='value';
        // Linux/macOS (bash): VAR=value <cmd>
        var isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        // Rely on default compose.yml in project directory to avoid quoting issues with -f
        var baseCmd = $"{tool} --project-directory '{projectDir}' {args}";
        if (isWin)
            return $"$env:COMPOSE_PROJECT_NAME='{projectName}'; {baseCmd}";
        return $"COMPOSE_PROJECT_NAME={projectName} {baseCmd}";
    }

    private static string ComposeCmdDefaultProject(string tool, string projectDir, string args)
    {
        // Do not override COMPOSE_PROJECT_NAME; let engine derive it from directory name
        var baseCmd = $"{tool} --project-directory '{projectDir}' {args}";
        return baseCmd;
    }

    private static void EnsureS5PortsAreFree(string composeTool)
    {
        var ports = new[] { MongoPort, WeaviatePort, OllamaPort, ApiPort };
        foreach (var port in ports)
        {
            if (!IsPortOpen(port)) continue;
            Console.WriteLine($"Port {port} is in use — attempting to free it by stopping containers that publish it.");
            TryStopContainersPublishingPort(composeTool, port);
        }

        // Re-check and fail early with diagnostics if still busy
        var stillBusy = ports.Where(IsPortOpen).ToArray();
        if (stillBusy.Length > 0)
        {
            Console.WriteLine("Ports still busy after cleanup: " + string.Join(", ", stillBusy));
            DumpContainerPs(composeTool);
            throw new InvalidOperationException("Required ports are in use and could not be freed: " + string.Join(", ", stillBusy));
        }
    }

    private static void DumpContainerPs(string composeTool)
    {
        var engine = composeTool.StartsWith("podman") ? "podman" : "docker";
        var ps = RunShell($"{engine} ps --format '{{{{.ID}}}} {{{{.Image}}}} {{{{.Ports}}}} {{{{.Names}}}}'");
        Console.WriteLine($"{engine} ps:\n" + ps.Stdout);
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

    private static void TryStopContainersPublishingPort(string composeTool, int port)
    {
        var engine = composeTool.StartsWith("podman") ? "podman" : "docker";
        var ps = RunShell($"{engine} ps --no-trunc --format '{{{{.ID}}}}|{{{{.Ports}}}}|{{{{.Names}}}}'");
        if (ps.ExitCode != 0)
        {
            Console.WriteLine($"{engine} ps failed: {ps.Stderr}");
            return;
        }
        var lines = ps.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            var id = parts[0].Trim();
            var ports = parts[1];
            if (ports.Contains($":{port}->"))
                matches.Add(id);
        }
        foreach (var id in matches.Distinct())
        {
            Console.WriteLine($"Stopping container {id} publishing :{port}...");
            var stop = RunShell($"{engine} rm -f {id}");
            if (stop.ExitCode != 0)
                Console.WriteLine($"Failed to remove container {id}: {stop.Stderr}");
        }
    }

    private static string GetRepoRootFromSource([CallerFilePath] string sourceFile = "")
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
            throw new InvalidOperationException("CallerFilePath not available");
        var srcDir = Path.GetDirectoryName(sourceFile)!; // tests/<Project>
        return Path.GetFullPath(Path.Combine(srcDir, "..", "..")); // repo root
    }
}
