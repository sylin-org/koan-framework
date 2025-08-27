using System.Net;
using System.Net.Sockets;
using Sora.Orchestration.Cli.Infrastructure;
using Sora.Orchestration.Models;
using Sora.Orchestration.Planning;

namespace Sora.Orchestration.Cli.Planning;

internal static class Planner
{
    // Captures the most recent host/container port assignment per app service for UX reporting
    internal static readonly Dictionary<string, (int Host, int Container, string Source)> LastPortAssignments
        = new(StringComparer.OrdinalIgnoreCase);

    public static Plan Build(Profile profile)
    {
        // 1) Descriptor file (YAML/JSON) if present → highest precedence
        if (TryLoadDescriptor(profile, out var fromFile))
        {
            return fromFile!;
        }
        // 2) Discovery-driven draft (CLI scans assemblies for manifests)
        var draft = ProjectDependencyAnalyzer.DiscoverDraft(profile);
        if (draft is not null)
        {
            var overrides = Overrides.Load();
            if (overrides is not null)
                draft = Overrides.Apply(draft, overrides);
            return FromDraft(profile, draft);
        }
        // No back-compat fallback: return an empty plan when nothing discovered
        return new Plan(profile, new List<ServiceSpec>());
    }

    // Demo plan removed (no back-compat). If nothing is discovered, Build() returns an empty plan.

    /// <summary>
    /// For non-Prod profiles, skip backing services whose host ports are already in use.
    /// Also rewrites the app (api) env to point to localhost for the skipped services and drops depends_on edges.
    /// Returns a new Plan and outputs the skipped service ids.
    /// </summary>
    public static Plan ApplyPortConflictSkip(Plan plan, Profile profile, out List<string> skippedServiceIds)
    {
        skippedServiceIds = new List<string>();
        if (profile == Profile.Prod) return plan; // never skip in prod

        // Identify conflicting ports
        var hostPorts = plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)).Distinct().ToList();
        var conflicts = FindConflictingPorts(hostPorts).ToHashSet();
        if (conflicts.Count == 0) return plan;

        // Choose services to skip (any with a conflicting host port), but never skip the app (api)
        var toSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in plan.Services)
        {
            if (string.Equals(s.Id, "api", StringComparison.OrdinalIgnoreCase)) continue;
            if (s.Ports.Any(p => conflicts.Contains(p.Host))) toSkip.Add(s.Id);
        }
        if (toSkip.Count == 0) return plan;

        skippedServiceIds = toSkip.ToList();

        // Filter services
        var kept = plan.Services.Where(s => !toSkip.Contains(s.Id)).ToList();

        // Rewrite api env and depends_on if present
        var apiIndex = kept.FindIndex(s => string.Equals(s.Id, "api", StringComparison.OrdinalIgnoreCase));
        if (apiIndex >= 0)
        {
            var api = kept[apiIndex];
            // Patch env values: replace "://{id}:" with "://localhost:" for each skipped id
            var env = new Dictionary<string, string?>(api.Env, StringComparer.Ordinal);
            foreach (var k in env.Keys.ToList())
            {
                var val = env[k];
                if (val is null) continue;
                var newVal = val;
                foreach (var id in toSkip)
                {
                    newVal = newVal.Replace("://" + id + ":", "://localhost:", StringComparison.OrdinalIgnoreCase);
                }
                env[k] = newVal;
            }
            // Drop depends_on for skipped services
            var deps = api.DependsOn.Where(d => !toSkip.Contains(d)).ToArray();
            kept[apiIndex] = api with { Env = env, DependsOn = deps };
        }

        return new Plan(plan.Profile, kept);
    }

    public static IReadOnlyList<int> FindConflictingPorts(IEnumerable<int> ports)
    {
        var conflicts = new List<int>();
        foreach (var port in ports.Distinct())
        {
            try
            {
                using var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
            }
            catch (SocketException)
            {
                conflicts.Add(port);
            }
        }
        return conflicts;
    }

    // Assign the public host port for the app (id=="api") and avoid publishing adapter ports.
    // Precedence: explicitPort (from CLI) > .sora manifest AssignedPublicPort > generated/attribute default > deterministic [30000,50000].
    public static Plan AssignAppPublicPort(Plan plan, int? explicitPort = null, bool exposeInternals = false, bool persist = true)
    {
        if (plan.Services.Count == 0) return plan;
        LastPortAssignments.Clear();
        var services = plan.Services.ToList();
        var cwd = Directory.GetCurrentDirectory();
        int? defaultFromCode = null;
        // Try to read generated manifest app.defaultPublicPort via ProjectDependencyAnalyzer to avoid duplication
        try
        {
            var draft = ProjectDependencyAnalyzer.DiscoverDraft(Profile.Local);
            // We used draft.AppHttpPort for historical; keep it as default when present
            if (draft is not null && draft.IncludeApp && draft.AppHttpPort > 0)
                defaultFromCode = draft.AppHttpPort;
        }
        catch { }

        var lm = LaunchManifest.Load(cwd);

        // Select primary app(s): heuristic — service with ASPNETCORE_URLS or id=="api"
        var appCandidates = services
            .Select((s, idx) => (s, idx))
            .Where(t => t.s.Env.ContainsKey("ASPNETCORE_URLS") || string.Equals(t.s.Id, "api", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (appCandidates.Count == 0)
        {
            // fallback to first service as app
            appCandidates.Add((services[0], 0));
        }

        foreach (var (app, appIdx) in appCandidates)
        {
            var containerPort = app.Ports.FirstOrDefault().Container;
            if (containerPort == 0) containerPort = 8080;
            // Per-service desired from launch manifest allocations if present
            int? desired = explicitPort;
            string source;
            if (desired is null)
            {
                if (lm?.Allocations is not null && lm.Allocations.TryGetValue(app.Id, out var allocFound) && allocFound is not null)
                {
                    desired = allocFound.AssignedPublicPort;
                    source = "launch-alloc";
                }
                else
                {
                    desired = lm?.App.AssignedPublicPort;
                    source = desired is not null ? "launch-app" : (defaultFromCode is not null ? "code-default" : "deterministic");
                }
            }
            else source = "flag";
            desired ??= defaultFromCode;
            var assigned = PickAvailable(desired ?? DeterministicFor(app.Id));
            services[appIdx] = app with { Ports = new List<(int, int)> { (assigned, containerPort) } };
            // Track for UX reporting
            LastPortAssignments[app.Id] = (assigned, containerPort, source);
            if (persist)
            {
                try
                {
                    lm ??= new LaunchManifest.Model();
                    lm.App.Id ??= app.Id;
                    lm.App.Name ??= new DirectoryInfo(cwd).Name;
                    lm.App.DefaultPublicPort ??= defaultFromCode;
                    lm.App.AssignedPublicPort ??= assigned; // keep top-level legacy assignment
                    if (lm.Allocations is null) lm.Allocations = new(StringComparer.OrdinalIgnoreCase);
                    if (!lm.Allocations.TryGetValue(app.Id, out var alloc))
                        lm.Allocations[app.Id] = alloc = new LaunchManifest.Model.Allocation();
                    alloc.AssignedPublicPort = assigned;
                    LaunchManifest.Save(cwd, lm);
                }
                catch { }
            }
        }
        int PickAvailable(int desired)
        {
            if (!IsBusy(desired)) return desired;
            for (var i = desired + 1; i <= 50000; i++) if (!IsBusy(i)) return i;
            for (var i = 30000; i < desired; i++) if (!IsBusy(i)) return i;
            // last resort: random
            var rnd = new Random();
            for (int i = 0; i < 1000; i++) { var p = rnd.Next(30000, 50001); if (!IsBusy(p)) return p; }
            return desired; // give up
        }
        bool IsBusy(int port)
        {
            try { using var l = new TcpListener(IPAddress.Loopback, port); l.Start(); l.Stop(); return false; } catch { return true; }
        }
        int DeterministicFor(string serviceId)
        {
            var key = cwd + ":" + serviceId;
            var seed = Fnv1a32(key);
            var basePort = 30000 + (int)(seed % 20001);
            return PickAvailable(basePort);
        }

        // For non-app services, if not exposing internals, drop host publish but keep container port reference for env formatting logic (map X:X -> X:X is fine for now)
        if (!exposeInternals)
        {
            for (int i = 0; i < services.Count; i++)
            {
                if (appCandidates.Any(c => c.idx == i)) continue;
                var s = services[i];
                if (s.Ports.Count > 0)
                {
                    // convert to container-only (0:container) — compose exporter will skip publishing host when host==0
                    var newPorts = s.Ports.Select(p => (0, p.Container)).ToList();
                    services[i] = s with { Ports = newPorts };
                }
            }
        }

        return new Plan(plan.Profile, services);
    }

    internal static uint Fnv1a32(string s)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in s)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return hash;
        }
    }

    static bool TryLoadDescriptor(Profile profile, out Plan? plan)
    {
        // Supported files (first found wins): sora.orchestration.yml|yaml|json under project root
        var cwd = Directory.GetCurrentDirectory();
        var candidates = Constants.OrchestrationDescriptorCandidates
            .Select(name => Path.Combine(cwd, name));
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var spec = File.ReadAllText(path);
                var model = ParseDescriptor(spec, Path.GetExtension(path));
                if (model is not null)
                {
                    plan = ToPlan(model, profile);
                    return true;
                }
            }
            catch
            {
                // Ignore parse errors; fall back to discovery/demo.
            }
        }
        plan = null;
        return false;
    }

    static DescriptorModel? ParseDescriptor(string content, string ext)
    {
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.Json.JsonSerializer.Deserialize<DescriptorModel>(content, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        // Minimal YAML support: very small dependency-free parser for our simple structure.
        if (ext.Equals(".yml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            return SimpleYaml.Parse(content);
        }
        return null;
    }

    static Plan ToPlan(DescriptorModel model, Profile profile)
    {
        var services = new List<ServiceSpec>();
        foreach (var s in model.Services)
        {
            var env = s.Env ?? new Dictionary<string, string?>();
            var ports = (s.Ports ?? new List<string>())
                .Select(p => ParsePort(p))
                .Where(t => t is not null)
                .Select(t => t!.Value)
                .ToList();
            var volumes = (s.Volumes ?? new List<string>())
                .Select(v => ParseVolume(v))
                .Where(t => t is not null)
                .Select(t => t!.Value)
                .ToList();
            HealthSpec? health = null;
            if (s.Health is not null)
            {
                health = new HealthSpec(
                    s.Health.Http,
                    s.Health.IntervalSeconds is null ? null : TimeSpan.FromSeconds(s.Health.IntervalSeconds.Value),
                    s.Health.TimeoutSeconds is null ? null : TimeSpan.FromSeconds(s.Health.TimeoutSeconds.Value),
                    s.Health.Retries
                );
            }
            services.Add(new ServiceSpec(s.Id, s.Image ?? string.Empty, env, ports, volumes, health, null, s.DependsOn?.ToArray() ?? Array.Empty<string>()));
        }
        return new Plan(profile, services);
    }

    static (int Host, int Container)? ParsePort(string s)
    {
        // formats: "8080:80" or "80" (maps to 80:80)
        var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var p)) return (p, p);
        if (parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var c)) return (h, c);
        return null;
    }

    static (string Source, string Target, bool Named)? ParseVolume(string s)
    {
        // formats: name:/target (named), ./host:/target (bind), /abs:/target (bind). We only track name/binds and pass named flag when name is alphanumeric without path chars.
        var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        var source = parts[0];
        var target = parts[1];
        var named = !(source.StartsWith(".") || source.StartsWith("/") || source.Contains('\\'));
        return (source, target, named);
    }

    internal sealed class Overrides
    {
        public string? Mode { get; set; } // Container | Local | Auto (future)
        public Dictionary<string, Service>? Services { get; set; }
        public sealed class Service
        {
            public string? Image { get; set; }
            public Dictionary<string, string?>? Env { get; set; }
            public List<string>? Volumes { get; set; }
        }

        public static Overrides? Load()
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                foreach (var rel in Constants.OverrideCandidates)
                {
                    var path = Path.Combine(cwd, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) continue;
                    var json = File.ReadAllText(path);
                    return System.Text.Json.JsonSerializer.Deserialize<Overrides>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch { }
            return null;
        }

        public static PlanDraft Apply(PlanDraft draft, Overrides ov)
        {
            var list = new List<ServiceRequirement>(draft.Services.Count);
            foreach (var s in draft.Services)
            {
                var id = s.Id;
                Overrides.Service? svc = null;
                if (ov.Services is not null) ov.Services.TryGetValue(id, out svc);
                var img = svc is not null && !string.IsNullOrWhiteSpace(svc.Image) ? svc.Image! : s.Image;
                var env = new Dictionary<string, string?>(s.Env);
                if (svc is not null && svc.Env is not null)
                {
                    foreach (var kv in svc.Env) env[kv.Key] = kv.Value;
                }
                var volumes = new List<string>(s.Volumes);
                if (svc is not null && svc.Volumes is not null)
                {
                    foreach (var v in svc.Volumes) if (!string.IsNullOrWhiteSpace(v)) volumes.Add(v);
                }
                list.Add(new ServiceRequirement(
                    id,
                    img,
                    env,
                    s.ContainerPorts,
                    volumes,
                    s.AppEnv,
                    s.Type,
                    // Keep endpoint data as-is; mode selection for token replacement will be extended in future
                    s.EndpointScheme,
                    s.EndpointHost,
                    s.EndpointUriPattern,
                    s.LocalScheme,
                    s.LocalHost,
                    s.LocalPort,
                    s.LocalUriPattern
                ));
            }
            return new PlanDraft(list, draft.IncludeApp, draft.AppHttpPort);
        }
    }

    // Descriptor model (intentionally minimal)
    private sealed class DescriptorModel
    {
        public List<Service> Services { get; set; } = new();
        public sealed class Service
        {
            public string Id { get; set; } = string.Empty;
            public string? Image { get; set; }
            public Dictionary<string, string?>? Env { get; set; }
            public List<string>? Ports { get; set; }
            public List<string>? Volumes { get; set; }
            public List<string>? DependsOn { get; set; }
            public ServiceHealth? Health { get; set; }
        }
        public sealed class ServiceHealth
        {
            public string? Http { get; set; }
            public int? IntervalSeconds { get; set; }
            public int? TimeoutSeconds { get; set; }
            public int? Retries { get; set; }
        }
    }

    // Micro YAML: supports a super small subset enough for our descriptor (services list with simple fields)
    private static class SimpleYaml
    {
        public static DescriptorModel Parse(string content)
        {
            var model = new DescriptorModel();
            DescriptorModel.Service? current = null;
            foreach (var raw in content.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                if (!char.IsWhiteSpace(raw[0]))
                {
                    // top-level keys ignored except for "services:"
                    continue;
                }
                var t = raw.TrimStart();
                var indent = raw.Length - t.Length;
                if (indent == 2 && t.StartsWith("- "))
                {
                    current = new DescriptorModel.Service();
                    model.Services.Add(current);
                    t = t[2..];
                    ApplyKeyValue(current, t);
                }
                else if (indent >= 4 && current is not null)
                {
                    ApplyKeyValue(current, t);
                }
            }
            return model;
        }

        static void ApplyKeyValue(DescriptorModel.Service s, string line)
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) return;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            switch (key)
            {
                case "id": s.Id = val.Trim('"'); break;
                case "image": s.Image = val.Trim('"'); break;
                case "ports": s.Ports = ParseList(val, s.Ports); break;
                case "volumes": s.Volumes = ParseList(val, s.Volumes); break;
                case "dependsOn": s.DependsOn = ParseList(val, s.DependsOn); break;
                case "env": s.Env = ParseMap(val, s.Env); break;
                case "health.http": EnsureHealth(s).Http = val.Trim('"'); break;
                case "health.intervalSeconds": EnsureHealth(s).IntervalSeconds = int.TryParse(val, out var i) ? i : null; break;
                case "health.timeoutSeconds": EnsureHealth(s).TimeoutSeconds = int.TryParse(val, out var o) ? o : null; break;
                case "health.retries": EnsureHealth(s).Retries = int.TryParse(val, out var r) ? r : null; break;
            }
        }

        static DescriptorModel.ServiceHealth EnsureHealth(DescriptorModel.Service s)
            => s.Health ??= new DescriptorModel.ServiceHealth();

        static List<string> ParseList(string val, List<string>? existing)
        {
            var list = existing ?? new List<string>();
            if (val.StartsWith("["))
            {
                // inline list: ["8080:80", "5432:5432"]
                val = val.Trim('[', ']');
                foreach (var part in val.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    list.Add(part.Trim().Trim('"'));
            }
            else if (val.StartsWith("- "))
            {
                list.Add(val[2..].Trim().Trim('"'));
            }
            return list;
        }

        static Dictionary<string, string?> ParseMap(string val, Dictionary<string, string?>? existing)
        {
            var map = existing ?? new Dictionary<string, string?>();
            if (val.StartsWith("{"))
            {
                // inline map: { KEY: "VALUE" }
                val = val.Trim('{', '}');
                foreach (var part in val.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':', 2);
                    if (kv.Length == 2)
                        map[kv[0].Trim()] = kv[1].Trim().Trim('"');
                }
            }
            return map;
        }
    }

    internal static Plan FromDraft(Profile profile, PlanDraft draft)
    {
        var services = new List<ServiceSpec>();

        // Compute inter-service dependencies based on Provides/Consumes tokens
        // Build token -> provider ids map
        var providersByToken = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in draft.Services)
        {
            if (r.Provides is null) continue;
            foreach (var token in r.Provides)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                if (!providersByToken.TryGetValue(token, out var list))
                {
                    list = new List<string>();
                    providersByToken[token] = list;
                }
                if (!list.Contains(r.Id, StringComparer.OrdinalIgnoreCase)) list.Add(r.Id);
            }
        }

        // Backing services from draft
        foreach (var r in draft.Services)
        {
            var ports = r.ContainerPorts.Select(p => (p, p)).ToList();
            var vols = new List<(string, string, bool)>();
            foreach (var v in r.Volumes)
            {
                var parsed = ParseVolume(v);
                if (parsed is { }) vols.Add(parsed.Value);
            }
            HealthSpec? health = null;
            if (!string.IsNullOrWhiteSpace(r.HealthHttpPath))
            {
                // Only create HTTP health checks for http/https schemes.
                var scheme = string.IsNullOrEmpty(r.EndpointScheme) ? "http" : r.EndpointScheme;
                if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    // Build an HTTP endpoint using container mode defaults: http(s)://{id}:{port}{path}
                    var port = r.ContainerPorts.FirstOrDefault();
                    var http = scheme + "://" + (string.IsNullOrEmpty(r.EndpointHost) ? r.Id : r.EndpointHost) + ":" + (port == 0 ? 80 : port) + r.HealthHttpPath;
                    health = new HealthSpec(
                        HttpEndpoint: http,
                        Interval: r.HealthIntervalSeconds is null ? null : TimeSpan.FromSeconds(r.HealthIntervalSeconds.Value),
                        Timeout: r.HealthTimeoutSeconds is null ? null : TimeSpan.FromSeconds(r.HealthTimeoutSeconds.Value),
                        Retries: r.HealthRetries
                    );
                }
            }

            // Determine depends_on from consumed tokens
            var depends = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (r.Consumes is not null)
            {
                foreach (var token in r.Consumes)
                {
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    if (!providersByToken.TryGetValue(token, out var provs) || provs is null) continue;
                    foreach (var pid in provs)
                    {
                        if (!string.Equals(pid, r.Id, StringComparison.OrdinalIgnoreCase)) depends.Add(pid);
                    }
                }
            }

            services.Add(new ServiceSpec(r.Id, r.Image, new Dictionary<string, string?>(r.Env), ports, vols, health, r.Type, depends.ToArray()));
        }

        // Optionally include app service with env derived from manifests
        if (draft.IncludeApp)
        {
            var cwd = Directory.GetCurrentDirectory();
            var appName = new DirectoryInfo(cwd).Name.ToLowerInvariant();
            var image = $"sora-{appName}:dev";
            var depIds = services.Select(s => s.Id).ToArray();
            var appEnv = new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = profile == Profile.Local ? "Development" : null,
                ["ASPNETCORE_URLS"] = $"http://+:{draft.AppHttpPort}"
            };
            // Apply AppEnvironment tokens per requirement
            var overrides = Overrides.Load();
            var mode = overrides?.Mode;
            foreach (var r in draft.Services)
            {
                foreach (var kv in r.AppEnv)
                {
                    var val = kv.Value;
                    if (val is null) continue;
                    // Replace tokens with port/scheme/host from selected mode (default: container)
                    var useLocal = string.Equals(mode, "Local", StringComparison.OrdinalIgnoreCase);
                    var port = useLocal ? (r.LocalPort ?? r.ContainerPorts.FirstOrDefault()) : r.ContainerPorts.FirstOrDefault();
                    val = val
                        .Replace("{serviceId}", r.Id)
                        .Replace("{port}", port == 0 ? string.Empty : port.ToString())
                        .Replace("{scheme}", useLocal ? (r.LocalScheme ?? r.EndpointScheme ?? string.Empty) : (r.EndpointScheme ?? string.Empty))
                        .Replace("{host}", useLocal ? (r.LocalHost ?? r.Id) : (r.EndpointHost ?? r.Id));
                    appEnv[kv.Key] = val;
                }
            }
            services.Insert(0, new ServiceSpec(
                Id: "api",
                Image: image,
                Env: appEnv,
                Ports: new List<(int, int)> { (draft.AppHttpPort, draft.AppHttpPort) },
                Volumes: new List<(string, string, bool)>(),
                Health: null,
                Type: ServiceType.App,
                DependsOn: depIds
            ));
        }

        return new Plan(profile, services);
    }
}
