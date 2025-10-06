using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Koan.Orchestration.Abstractions;

namespace Koan.Orchestration.Connector.Docker;

public sealed partial class DockerProvider : IHostingProvider
{
    private const string DockerCli = "docker";

    public string Id => "docker";
    public int Priority => 100; // Windows-first default

    public async Task<(bool Ok, string? Reason)> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var (code, _, err) = await Run(DockerCli, "version --format '{{.Server.Version}}'", ct);
            if (code == 0) return (true, null);
            return (false, string.IsNullOrWhiteSpace(err) ? "docker not available" : err.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task Up(string composePath, Profile profile, RunOptions options, CancellationToken ct = default)
    {
        var detach = options.Detach ? "-d" : string.Empty;
        var (_, _, err) = await Run(DockerCli, $"compose -f \"{composePath}\" up {detach}", ct);
        if (!string.IsNullOrWhiteSpace(err))
        {
            // docker writes a lot to stderr even on success; don't fail solely on err content.
        }
        // Readiness wait: poll compose ps until all services are running (and healthy when health is present)
        if (options.ReadinessTimeout is { } timeout && timeout > TimeSpan.Zero)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                while (true)
                {
                    if (cts.IsCancellationRequested)
                        throw new TimeoutException($"services not ready within {timeout.TotalSeconds:n0}s");
                    try
                    {
                        var services = await ComposeStatusForFile(composePath, cts.Token);
                        if (services.Count == 0)
                        {
                            await Task.Delay(300, cts.Token);
                            continue;
                        }
                        var allOk = services.All(s =>
                            string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase) &&
                            (s.Health is null || string.Equals(s.Health, "healthy", StringComparison.OrdinalIgnoreCase)));
                        if (allOk) break;
                    }
                    catch
                    {
                        // ignore transient errors during readiness
                    }
                    await Task.Delay(500, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"services not ready within {timeout.TotalSeconds:n0}s");
            }
        }
    }

    private async Task<List<(string Service, string State, string? Health)>> ComposeStatusForFile(string composePath, CancellationToken ct)
    {
        try
        {
            var (code, stdout, _) = await Run(DockerCli, $"compose -f \"{composePath}\" ps --format json", ct);
            if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return new();
            return ParseComposePsJson(stdout);
        }
        catch
        {
            return new();
        }
    }

    public async Task Down(string composePath, StopOptions options, CancellationToken ct = default)
    {
        var vols = options.RemoveVolumes ? "-v" : string.Empty;
        await Run(DockerCli, $"compose -f \"{composePath}\" down {vols}", ct);
    }

    public async IAsyncEnumerable<string> Logs(LogsOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var svc = string.IsNullOrWhiteSpace(options.Service) ? string.Empty : options.Service + " ";
        var follow = options.Follow ? "-f" : string.Empty;
        var tail = options.Tail is { } t ? $"--tail {t}" : string.Empty;
        var since = string.IsNullOrWhiteSpace(options.Since) ? string.Empty : $"--since \"{options.Since}\"";
        await foreach (var line in Stream(DockerCli, $"compose logs {follow} {tail} {since} {svc}", ct))
        {
            yield return line;
        }
    }

    public async Task<ProviderStatus> Status(StatusOptions options, CancellationToken ct = default)
    {
        int code = -1;
        string stdout = string.Empty;
        try
        {
            var res = await Run(DockerCli, "compose ps --format json", ct);
            code = res.ExitCode;
            stdout = res.StdOut;
        }
        catch
        {
            // If docker is not installed or command fails to launch, treat as no services info.
        }
        var services = code == 0 && !string.IsNullOrWhiteSpace(stdout)
            ? ParseComposePsJson(stdout)
            : new List<(string Service, string State, string? Health)>();
        var (ok, _) = await IsAvailableAsync(ct);
        var engineVer = ok ? (await Run(DockerCli, "version --format '{{.Server.Version}}'", ct)).StdOut.Trim() : "";
        return new ProviderStatus(Id, engineVer, services);
    }

    public async Task<IReadOnlyList<PortBinding>> LivePorts(CancellationToken ct = default)
    {
        try
        {
            var (code, outText, _) = await Run(DockerCli, "compose ps --format json", ct);
            if (code != 0 || string.IsNullOrWhiteSpace(outText)) return Array.Empty<PortBinding>();
            return ParseComposePsPorts(outText);
        }
        catch
        {
            return Array.Empty<PortBinding>();
        }
    }

    public EngineInfo EngineInfo()
        => new("Docker", GetVersionSafe(), GetEndpointSafe());

    static string GetVersionSafe()
    {
        try { return Run(DockerCli, "version --format '{{.Server.Version}}'", CancellationToken.None).GetAwaiter().GetResult().StdOut.Trim(); }
        catch { return string.Empty; }
    }

    static string GetEndpointSafe()
    {
        try { return Run(DockerCli, "context show", CancellationToken.None).GetAwaiter().GetResult().StdOut.Trim(); }
        catch { return string.Empty; }
    }

    static async Task<(int ExitCode, string StdOut, string StdErr)> Run(string file, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sbOut = new System.Text.StringBuilder();
        var sbErr = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) sbOut.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) sbErr.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, sbOut.ToString(), sbErr.ToString());
    }

    static async IAsyncEnumerable<string> Stream(string file, string args, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Start();
        while (!p.HasExited)
        {
            var line = await p.StandardOutput.ReadLineAsync(ct);
            if (line is null) break;
            yield return line;
        }
        // Drain remaining lines
        while (true)
        {
            var rest = await p.StandardOutput.ReadLineAsync(ct);
            if (rest is null) break;
            yield return rest;
        }
    }

    internal static List<(string Service, string State, string? Health)> ParseComposePsJson(string stdout)
    {
        var result = new List<(string Service, string State, string? Health)>();
        if (string.IsNullOrWhiteSpace(stdout)) return result;
        try
        {
            // Docker may emit either a JSON array or NDJSON (one JSON object per line)
            var trimmed = stdout.TrimStart();
            if (trimmed.StartsWith("["))
            {
                var arr = JArray.Parse(stdout);
                foreach (var token in arr)
                {
                    if (token is not JObject o) continue;
                    var name = o["Name"]?.Value<string>();
                    var state = o["State"]?.Value<string>();
                    var health = o["Health"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(health)) health = null; // normalize empty to null (no healthcheck)
                    if (!string.IsNullOrEmpty(name))
                        result.Add((name!, state ?? string.Empty, health));
                }
            }
            else
            {
                // NDJSON: parse each JSON object line independently
                var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (!l.StartsWith("{")) continue;
                    try
                    {
                        var o = JToken.Parse(l) as JObject;
                        if (o is null) continue;
                        var name = o["Name"]?.Value<string>();
                        var state = o["State"]?.Value<string>();
                        var health = o["Health"]?.Value<string>();
                        if (string.IsNullOrWhiteSpace(health)) health = null;
                        if (!string.IsNullOrEmpty(name))
                            result.Add((name!, state ?? string.Empty, health));
                    }
                    catch { /* ignore bad lines */ }
                }
            }
        }
        catch
        {
            // ignore parse errors; return what we have
        }
        return result;
    }

    internal static IReadOnlyList<PortBinding> ParseComposePsPorts(string stdout)
    {
        var list = new List<PortBinding>();
        if (string.IsNullOrWhiteSpace(stdout)) return list;
        try
        {
            var trimmed = stdout.TrimStart();
            if (trimmed.StartsWith("["))
            {
                var arr = JArray.Parse(stdout);
                ExtractPortsFromJsonArray(arr, list);
            }
            else
            {
                var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (!l.StartsWith("{")) continue;
                    try
                    {
                        var o = JToken.Parse(l) as JObject;
                        if (o is null) continue;
                        ExtractPortsFromJsonArray(new JArray(o), list);
                    }
                    catch { }
                }
            }
        }
        catch { }
        return list;
    }

}

public sealed partial class DockerProvider
{
    private static void ExtractPortsFromJsonArray(JArray root, List<PortBinding> list)
    {
        foreach (var token in root)
        {
            if (token is not JObject el) continue;
            var name = el["Name"]?.Value<string>();
            if (string.IsNullOrEmpty(name)) continue;
            var portsTok = el["Ports"];
            // Docker compose ps --format json uses strings like "0.0.0.0:8080->80/tcp, :::8080->80/tcp"
            if (portsTok is JValue v && v.Type == JTokenType.String)
            {
                foreach (var binding in ParsePortsString(v.Value<string>()!, name!)) list.Add(binding);
            }
            else if (portsTok is JArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JValue sv && sv.Type == JTokenType.String)
                        foreach (var b in ParsePortsString(sv.Value<string>()!, name!)) list.Add(b);
                }
            }
        }
    }

    private static IEnumerable<PortBinding> ParsePortsString(string ports, string service)
    {
        // Example segments: "0.0.0.0:8080->80/tcp", ":::8080->80/tcp"
        var segments = ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var seg in segments)
        {
            // Split address:host->container/proto
            // Address may be omitted; default localhost
            var arrowIdx = seg.IndexOf("->", StringComparison.Ordinal);
            if (arrowIdx < 0) continue;
            var left = seg.Substring(0, arrowIdx);
            var right = seg.Substring(arrowIdx + 2);
            var proto = "tcp";
            var slashIdx = right.IndexOf('/', StringComparison.Ordinal);
            if (slashIdx >= 0)
            {
                proto = right.Substring(slashIdx + 1).Trim();
                right = right.Substring(0, slashIdx);
            }
            // left: may be "0.0.0.0:8080" or just "8080"
            string? address = null;
            int hostPort;
            var colonIdx = left.LastIndexOf(':');
            if (colonIdx >= 0)
            {
                address = left.Substring(0, colonIdx).Trim();
                var hostStr = left.Substring(colonIdx + 1).Trim();
                if (!int.TryParse(hostStr, out hostPort)) continue;
            }
            else
            {
                if (!int.TryParse(left.Trim(), out hostPort)) continue;
            }
            if (!int.TryParse(right.Trim(), out var container)) continue;
            yield return new PortBinding(service, hostPort, container, proto, string.IsNullOrWhiteSpace(address) ? null : address);
        }
    }
}


