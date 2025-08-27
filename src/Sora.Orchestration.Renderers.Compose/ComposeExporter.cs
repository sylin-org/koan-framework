using Sora.Orchestration;
using System.Reflection;
using System.Text;
using Sora.Orchestration.Abstractions;
using Sora.Orchestration.Attributes;
using Sora.Orchestration.Infrastructure;
using Sora.Orchestration.Models;

namespace Sora.Orchestration.Renderers.Compose;

public sealed class ComposeExporter : IArtifactExporter
{
    public string Id => "compose";
    public ExporterCapabilities Capabilities => new(false, true, false);
    public bool Supports(string format) => string.Equals(format, "compose", StringComparison.OrdinalIgnoreCase);

    public async Task GenerateAsync(Plan plan, Profile profile, string outPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(outPath)) throw new ArgumentException("Output path required", nameof(outPath));

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var composeDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;

        var yaml = new StringBuilder();
        // Compose v2+ typically omits the top-level version field.
        var namedVolumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Discover adapter-declared image prefixes and host mount container paths
        var mountMap = DiscoverHostMounts();

        // Define networks first
        yaml.AppendLine("networks:");
        yaml.AppendLine($"  {OrchestrationConstants.InternalNetwork}:");
        yaml.AppendLine("    internal: true");
        yaml.AppendLine($"  {OrchestrationConstants.ExternalNetwork}: {{}}");
        yaml.AppendLine();
        yaml.AppendLine("services:");
        // Pre-compute which services have HTTP healthchecks
        var healthyIds = new HashSet<string>(plan.Services
            .Where(s => s.Health is not null && !string.IsNullOrWhiteSpace(s.Health.HttpEndpoint))
            .Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var svc in plan.Services)
        {
            // Ensure host mounts for persistence are present according to profile:
            // - Local/Staging: bind mounts (./Data/{service} -> /container/path)
            // - CI: named volumes (data_{service} -> /container/path)
            // - Prod: do not inject mounts
            var enriched = EnsureHostMounts(svc, mountMap, profile);
            WriteService(yaml, enriched, healthyIds, namedVolumes, indent: 2, composeDir);
        }

        if (namedVolumes.Count > 0)
        {
            yaml.AppendLine("volumes:");
            foreach (var v in namedVolumes.OrderBy(x => x))
            {
                yaml.Append("  ").Append(v).AppendLine(": {}");
            }
        }

        await File.WriteAllTextAsync(outPath, yaml.ToString(), ct);
    }

    static Dictionary<string, List<string>> DiscoverHostMounts()
    {
        try
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a =>
                {
                    try { var n = a.GetName().Name; return n is not null && n.StartsWith("Sora.", StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                })
                .ToArray();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t is null) continue;
                    HostMountAttribute[] mounts;
                    try { mounts = t.GetCustomAttributes(typeof(HostMountAttribute), inherit: false).Cast<HostMountAttribute>().ToArray(); }
                    catch { continue; }
                    if (mounts.Length == 0) continue;

                    // Prefer ContainerDefaultsAttribute.Image as the image prefix source
                    ContainerDefaultsAttribute? containerDefaults = null;
                    try
                    {
                        containerDefaults = t.GetCustomAttributes(typeof(ContainerDefaultsAttribute), inherit: false)
                            .Cast<ContainerDefaultsAttribute>()
                            .FirstOrDefault();
                    }
                    catch { }

                    if (containerDefaults is not null && !string.IsNullOrWhiteSpace(containerDefaults.Image))
                    {
                        var prefix = containerDefaults.Image;
                        if (!map.TryGetValue(prefix, out var list))
                        {
                            list = new List<string>();
                            map[prefix] = list;
                        }
                        foreach (var m in mounts)
                        {
                            if (!list.Contains(m.ContainerPath, StringComparer.OrdinalIgnoreCase))
                                list.Add(m.ContainerPath);
                        }
                        continue;
                    }

                    // Back-compat: use DefaultEndpointAttribute.ImagePrefixes if present
                    DefaultEndpointAttribute[] endpoints;
                    try { endpoints = t.GetCustomAttributes(typeof(DefaultEndpointAttribute), inherit: false).Cast<DefaultEndpointAttribute>().ToArray(); }
                    catch { continue; }
                    if (endpoints.Length == 0) continue;
                    foreach (var ep in endpoints)
                    {
                        if (ep.ImagePrefixes is null || ep.ImagePrefixes.Length == 0) continue;
                        foreach (var prefix in ep.ImagePrefixes)
                        {
                            if (string.IsNullOrWhiteSpace(prefix)) continue;
                            if (!map.TryGetValue(prefix, out var list))
                            {
                                list = new List<string>();
                                map[prefix] = list;
                            }
                            foreach (var m in mounts)
                            {
                                if (!list.Contains(m.ContainerPath, StringComparer.OrdinalIgnoreCase))
                                    list.Add(m.ContainerPath);
                            }
                        }
                    }
                }
            }
            return map;
        }
        catch
        {
            // Discovery is best-effort. If runtime reflection encounters missing dependencies, fall back to heuristics.
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static ServiceSpec EnsureHostMounts(ServiceSpec svc, Dictionary<string, List<string>> mountMap, Profile profile)
    {
        // If svc.Volumes already contains binds or named volumes, keep them; we only add host binds for common data paths
        // Convention: map to ./Data/{service} ensuring relative path is stable
        var vols = svc.Volumes.ToList();
        bool HasTarget(string target) => vols.Any(v => string.Equals(v.Target, target, StringComparison.OrdinalIgnoreCase));

        // In prod, do not inject any persistence mounts automatically
        if (profile == Profile.Prod)
            return svc;

        // Helper to add a mapping depending on profile
        void AddForTarget(string target)
        {
            if (HasTarget(target)) return;
            if (profile == Profile.Ci)
            {
                // CI prefers ephemeral named volumes to avoid host FS coupling
                var volName = $"data_{svc.Id}";
                vols.Add((Source: volName, Target: target, Named: true));
            }
            else
            {
                // Local/Staging: bind mount to ./Data/{service}
                vols.Add((Source: $"./Data/{svc.Id}", Target: target, Named: false));
            }
        }

        // Find any prefix mapping that matches the image
        var added = false;
        if (!string.IsNullOrWhiteSpace(svc.Image))
        {
            foreach (var kv in mountMap)
            {
                if (svc.Image.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var target in kv.Value)
                    {
                        if (!HasTarget(target))
                        {
                            AddForTarget(target);
                            added = true;
                        }
                    }
                }
            }
        }
        // Fallback heuristics when no attribute-driven mapping matched
        if (!added)
        {
            var img = (svc.Image ?? string.Empty).ToLowerInvariant();
            if ((img.Contains("postgres") || img.Contains("postgresql")) && !HasTarget("/var/lib/postgresql/data"))
                AddForTarget("/var/lib/postgresql/data");
            else if (img.Contains("mongo") && !HasTarget("/data/db"))
                AddForTarget("/data/db");
            else if (img.Contains("redis") && !HasTarget("/data"))
                AddForTarget("/data");
            else if (img.Contains("sqlserver") && !HasTarget("/var/opt/mssql"))
                AddForTarget("/var/opt/mssql");
            else if (img.Contains("weaviate") && !HasTarget("/var/lib/weaviate"))
                AddForTarget("/var/lib/weaviate");
            else if (img.Contains("ollama") && !HasTarget("/root/.ollama"))
                AddForTarget("/root/.ollama");
        }
        return svc with { Volumes = vols };
    }

    static void WriteService(StringBuilder yaml, ServiceSpec svc, IReadOnlySet<string> healthyServiceIds, HashSet<string> namedVolumes, int indent, string composeDir)
    {
        var pad = new string(' ', indent);
        yaml.Append(pad).Append(svc.Id).AppendLine(":");
        yaml.Append(pad).Append("  image: ").AppendLine(EscapeScalar(svc.Image));

        // If this is the app service (convention id == "api") and we're in a project folder, emit a build block
        if (string.Equals(svc.Id, "api", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var hasProject = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).Any();
                if (hasProject)
                {
                    yaml.Append(pad).AppendLine("  build:");
                    // Prefer using the repository root as build context so project references outside the sample folder are available
                    var repoRoot = FindRepoRoot(cwd);
                    var contextDir = repoRoot ?? cwd;
                    var relContext = ToPosixPath(Path.GetRelativePath(composeDir, contextDir));
                    if (string.IsNullOrEmpty(relContext)) relContext = ".";
                    yaml.Append(pad).Append("    context: ").AppendLine(EscapeScalar(relContext));
                    // Prefer a Dockerfile if present; dockerfile path is relative to the context directory
                    var dockerfile = Directory.EnumerateFiles(cwd, "Dockerfile", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (!string.IsNullOrEmpty(dockerfile))
                    {
                        var relDockerfile = ToPosixPath(Path.GetRelativePath(contextDir, dockerfile));
                        yaml.Append(pad).Append("    dockerfile: ").AppendLine(EscapeScalar(relDockerfile));
                    }
                }
            }
            catch { /* best-effort */ }
        }

        if (svc.Env.Count > 0)
        {
            yaml.Append(pad).AppendLine("  environment:");
            foreach (var kvp in svc.Env)
            {
                if (kvp.Value is null) continue; // omit null entries
                                                 // Preserve ${VAR} style references unquoted so compose resolves from env/.env; otherwise quote to avoid YAML coercion.
                yaml.Append(pad).Append("    ").Append(kvp.Key).Append(": ").AppendLine(ToEnvYamlValue(kvp.Value));
            }
        }

        if (svc.Ports.Count > 0)
        {
            var any = false;
            var tmp = new StringBuilder();
            foreach (var (host, container) in svc.Ports)
            {
                if (host > 0)
                {
                    any = true;
                    tmp.Append(pad).Append("    - \"").Append(host).Append(':').Append(container).AppendLine("\"");
                }
            }
            if (any)
            {
                yaml.Append(pad).AppendLine("  ports:");
                yaml.Append(tmp);
            }
        }

        if (svc.Volumes.Count > 0)
        {
            yaml.Append(pad).AppendLine("  volumes:");
            foreach (var (source, target, named) in svc.Volumes)
            {
                if (named && !string.IsNullOrWhiteSpace(source)) namedVolumes.Add(source);
                yaml.Append(pad).Append("    - ").Append(source).Append(':').AppendLine(target);
            }
        }

        if (svc.Health is not null && !string.IsNullOrWhiteSpace(svc.Health.HttpEndpoint))
        {
            yaml.Append(pad).AppendLine("  healthcheck:");
            // Try curl first; if not installed, fall back to wget; finally try bash /dev/tcp to probe the port
            var hp = ParseHostPortFromUrl(svc.Health.HttpEndpoint);
            var tcpProbe = hp is null ? string.Empty : $" || bash -lc 'exec 3<>/dev/tcp/{hp.Value.host}/{hp.Value.port}'";
            var test = $"(curl -fsS {svc.Health.HttpEndpoint} || wget -q -O- {svc.Health.HttpEndpoint}{tcpProbe}) >/dev/null 2>&1 || exit 1";
            yaml.Append(pad).Append("    test: [\"CMD-SHELL\", \"").Append(EscapeJson(test)).AppendLine("\"]");
            if (svc.Health.Interval is not null)
                yaml.Append(pad).Append("    interval: ").AppendLine(ToDuration(svc.Health.Interval.Value));
            if (svc.Health.Timeout is not null)
                yaml.Append(pad).Append("    timeout: ").AppendLine(ToDuration(svc.Health.Timeout.Value));
            if (svc.Health.Retries is not null)
                yaml.Append(pad).Append("    retries: ").AppendLine(svc.Health.Retries.Value.ToString());
        }

        if (svc.DependsOn.Count > 0)
        {
            yaml.Append(pad).AppendLine("  depends_on:");
            foreach (var dep in svc.DependsOn)
            {
                yaml.Append(pad).Append("    ").Append(dep).AppendLine(":");
                // If the dependency service has no health, use service_started to avoid waiting on a nonexistent check.
                var cond = healthyServiceIds.Contains(dep) ? "service_healthy" : "service_started";
                yaml.Append(pad).Append("      condition: ").AppendLine(cond);
            }
        }

        // Networks: app on both internal/external; adapters only on internal
        yaml.Append(pad).AppendLine("  networks:");
        if (string.Equals(svc.Id, "api", StringComparison.OrdinalIgnoreCase))
        {
            yaml.Append(pad).Append("    - ").AppendLine(OrchestrationConstants.InternalNetwork);
            yaml.Append(pad).Append("    - ").AppendLine(OrchestrationConstants.ExternalNetwork);
        }
        else
        {
            yaml.Append(pad).Append("    - ").AppendLine(OrchestrationConstants.InternalNetwork);
        }
    }

    static string EscapeScalar(string value)
    {
        // Quote when value contains special YAML chars or starts with special tokens.
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Any(c => char.IsWhiteSpace(c) || ":{}[],&*#?|-<>=!%@\\\"'".Contains(c)))
        {
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
        return value;
    }

    static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static bool LooksLikeEnvSubstitution(string value)
        => value.Length >= 4 && value[0] == '$' && value[1] == '{' && value[^1] == '}';

    static string QuoteYaml(string? value)
    {
        if (value is null) return "\"\"";
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    static string ToEnvYamlValue(string value)
    {
        if (LooksLikeEnvSubstitution(value))
        {
            // Emit as-is (no quotes) so docker compose performs env substitution.
            return value;
        }
        return QuoteYaml(value);
    }

    static string ToDuration(TimeSpan ts)
    {
        // Compose accepts Go-like durations e.g. 10s, 1m, 2h. We'll emit seconds when possible.
        if (ts.TotalSeconds == Math.Round(ts.TotalSeconds))
            return $"{(int)ts.TotalSeconds}s";
        return $"{ts.TotalMilliseconds}ms";
    }

    static string? FindRepoRoot(string start)
    {
        try
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                // Heuristics: presence of solution file or a src/ folder marks the repo root
                var hasSln = dir.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any();
                var hasSrc = dir.EnumerateDirectories("src", SearchOption.TopDirectoryOnly).Any();
                if (hasSln || hasSrc)
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { }
        return null;
    }

    static string ToPosixPath(string path)
        => path.Replace('\\', '/');

    static (string host, int port)? ParseHostPortFromUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var port = uri.IsDefaultPort ? (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80) : uri.Port;
                return (uri.Host, port);
            }
        }
        catch { }
        return null;
    }
}
