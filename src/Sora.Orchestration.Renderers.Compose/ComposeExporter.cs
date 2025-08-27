using System.Reflection;
using System.Text;
using Sora.Orchestration;

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

    var yaml = new StringBuilder();
        // Compose v2+ typically omits the top-level version field.
        var namedVolumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Discover adapter-declared image prefixes and host mount container paths
    var mountMap = DiscoverHostMounts();

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
            WriteService(yaml, enriched, healthyIds, namedVolumes, indent: 2);
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
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
            catch { continue; }
            foreach (var t in types)
            {
                if (t is null) continue;
                var mounts = t.GetCustomAttributes(typeof(HostMountAttribute), inherit: false).Cast<HostMountAttribute>().ToArray();
                if (mounts.Length == 0) continue;

                // Prefer ContainerDefaultsAttribute.Image as the image prefix source
                var containerDefaults = t.GetCustomAttributes(typeof(Sora.Orchestration.Abstractions.Attributes.ContainerDefaultsAttribute), inherit: false)
                    .Cast<Sora.Orchestration.Abstractions.Attributes.ContainerDefaultsAttribute>()
                    .FirstOrDefault();

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
                var endpoints = t.GetCustomAttributes(typeof(DefaultEndpointAttribute), inherit: false).Cast<DefaultEndpointAttribute>().ToArray();
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

    static void WriteService(StringBuilder yaml, ServiceSpec svc, IReadOnlySet<string> healthyServiceIds, HashSet<string> namedVolumes, int indent)
    {
        var pad = new string(' ', indent);
        yaml.Append(pad).Append(svc.Id).AppendLine(":");
        yaml.Append(pad).Append("  image: ").AppendLine(EscapeScalar(svc.Image));

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
            yaml.Append(pad).AppendLine("  ports:");
            foreach (var (host, container) in svc.Ports)
            {
                yaml.Append(pad).Append("    - \"").Append(host).Append(':').Append(container).AppendLine("\"");
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
            var test = $"curl -fsS {svc.Health.HttpEndpoint} || exit 1";
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
}
