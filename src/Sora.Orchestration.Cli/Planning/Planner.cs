using System.Net;
using System.Net.Sockets;

namespace Sora.Orchestration.Cli.Planning;

internal static class Planner
{
    public static Plan Build(Profile profile)
    {
        // 1) Descriptor file (YAML/JSON) if present → highest precedence
        if (TryLoadDescriptor(profile, out var fromFile))
        {
            return fromFile!;
        }

        // 2) Env-driven prototype plans
        var provider = Get("Sora:Data:Provider") ?? Get("SORA_DATA_PROVIDER");
        if (string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            var pw = Get("Sora:Data:Postgres:Password") ?? Get("SORA_DATA_POSTGRES_PASSWORD") ?? "pw";
            return new Plan(
                profile,
                new[]
                {
                    new ServiceSpec(
                        Id: "db",
                        Image: "postgres:16",
                        Env: new Dictionary<string,string?>{ ["POSTGRES_PASSWORD"] = pw },
                        Ports: new List<(int,int)>{ (5432,5432) },
                        Volumes: new List<(string,string,bool)>{ ("pgdata", "/var/lib/postgresql/data", true) },
                        Health: new HealthSpec("http://localhost:5432/", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 5),
                        DependsOn: Array.Empty<string>()
                    )
                }
            );
        }
        if (string.Equals(provider, "redis", StringComparison.OrdinalIgnoreCase))
        {
            return new Plan(
                profile,
                new[]
                {
                    new ServiceSpec(
                        Id: "cache",
                        Image: "redis:7",
                        Env: new Dictionary<string,string?>(),
                        Ports: new List<(int,int)>{ (6379,6379) },
                        Volumes: Array.Empty<(string,string,bool)>(),
                        Health: new HealthSpec(null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 5),
                        DependsOn: Array.Empty<string>()
                    )
                }
            );
        }
        // Fallback to demo plan
        return Demo(profile);
    }

    static string? Get(string key) => Environment.GetEnvironmentVariable(key);

    static Plan Demo(Profile profile)
        => new(
            profile,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "postgres:16",
                    Env: new Dictionary<string,string?>{ ["POSTGRES_PASSWORD"] = "pw" },
                    Ports: new List<(int,int)>{ (5432,5432) },
                    Volumes: new List<(string,string,bool)>{ ("pgdata", "/var/lib/postgresql/data", true) },
                    Health: new HealthSpec("http://localhost:5432/", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 5),
                    DependsOn: Array.Empty<string>()
                )
            }
        );

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
                // Ignore parse errors; fall back to env/demo.
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
            services.Add(new ServiceSpec(s.Id, s.Image ?? string.Empty, env, ports, volumes, health, s.DependsOn?.ToArray() ?? Array.Empty<string>()));
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
}
