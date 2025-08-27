using System.Text.Json;
using System.Text.RegularExpressions;
using static Sora.Orchestration.Redaction;
using Sora.Orchestration;
using Sora.Orchestration.Renderers.Compose;
using Sora.Orchestration.Provider.Docker;
using Sora.Orchestration.Provider.Podman;
using Sora.Orchestration.Cli;
using Sora.Orchestration.Cli.Planning;
using Sora.Orchestration.Cli.Formatting;
using System.Reflection;
using System.Net;
using System.Net.Sockets;

// Minimal DX-first CLI: export, doctor, up, down, status, logs, inspect
// Flags: -v/-vv, --json (for explain/status/inspect), --dry-run, --explain

var (cmd, rest) = args.Length == 0
    ? ("inspect", Array.Empty<string>())
    : (args[0].ToLowerInvariant(), args.Skip(1).ToArray());

// Discover adapter-declared default endpoints and register a scheme resolver
RegisterSchemeResolver();

return cmd switch
{
    "export" => await ExportAsync(rest),
    "doctor" => await DoctorAsync(rest),
    "up"     => await UpAsync(rest),
    "down"   => await DownAsync(rest),
    "status" => await StatusAsync(rest),
    "logs"   => await LogsAsync(rest),
    "inspect"=> await InspectAsync(rest),
    _        => Help()
};

static int Help()
{
    Console.WriteLine("sora <command> [options]\nCommands: export, doctor, up, down, status, logs, inspect");
    return 1;
}

static void RegisterSchemeResolver()
{
    try
    {
        // Build a lookup of (imagePrefix, containerPort) -> (scheme, pattern) from DefaultEndpointAttribute across loaded assemblies
        var map = new Dictionary<(string Prefix, int Port), (string Scheme, string? Pattern)>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (var t in types)
            {
                if (t is null) continue;
                var attrs = t.GetCustomAttributes(typeof(DefaultEndpointAttribute), inherit: false).Cast<DefaultEndpointAttribute>();
                foreach (var a in attrs)
                {
                    if (a.ImagePrefixes is null || a.ImagePrefixes.Length == 0)
                        continue;
                    foreach (var prefix in a.ImagePrefixes)
                    {
                        if (string.IsNullOrWhiteSpace(prefix)) continue;
                        map[(prefix, a.ContainerPort)] = (a.Scheme, a.UriPattern);
                    }
                }
            }
        }

        // Resolvers prefer image-prefix matches; UriPattern takes precedence when present
        EndpointFormatter.UseEndpointResolver((serviceIdOrImage, containerPort) =>
        {
            if (!string.IsNullOrWhiteSpace(serviceIdOrImage))
            {
                foreach (var kv in map)
                {
                    if (serviceIdOrImage.StartsWith(kv.Key.Prefix, StringComparison.OrdinalIgnoreCase)
                        && kv.Key.Port == containerPort)
                        return (kv.Value.Scheme, kv.Value.Pattern);
                }
            }
            var scheme = EndpointSchemeFallback(serviceIdOrImage, containerPort);
            return (scheme, null);
        });

        // Also keep simple scheme resolver for any legacy call sites
        EndpointFormatter.UseSchemeResolver((serviceIdOrImage, containerPort) =>
        {
            if (!string.IsNullOrWhiteSpace(serviceIdOrImage))
            {
                foreach (var kv in map)
                {
                    if (serviceIdOrImage.StartsWith(kv.Key.Prefix, StringComparison.OrdinalIgnoreCase)
                        && kv.Key.Port == containerPort)
                        return kv.Value.Scheme;
                }
            }
            // fall through to EndpointFormatter's internal fallback by returning empty => caller will call InferSchemeByImage
            return EndpointSchemeFallback(serviceIdOrImage, containerPort);
        });
    }
    catch
    {
        // non-fatal; formatter will use its own fallback heuristics
    }
}

static string EndpointSchemeFallback(string serviceIdOrImage, int containerPort)
{
    // Duplicate minimal behavior: return empty to signal fallback; but EndpointFormatter expects a scheme.
    // We replicate the same heuristic here to keep deterministic behavior when no attribute match is found.
    var s = (serviceIdOrImage ?? string.Empty).ToLowerInvariant();
    if (s.Contains("postgres")) return "postgres";
    if (s.Contains("redis")) return "redis";
    if (s.Contains("mongo")) return "mongodb";
    if (s.Contains("elastic") || s.Contains("opensearch")) return containerPort == 443 ? "https" : "http";
    if (containerPort == 443) return "https";
    return containerPort is 80 or 8080 or 3000 or 5000 or 5050 or 4200 or 9200 ? "http" : "tcp";
}

static async Task<int> ExportAsync(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("export <compose> --out <path>");
        return 2;
    }
    var format = args[0].ToLowerInvariant();
    var outPath = FindArg(args, "--out") ?? Constants.DefaultComposePath;
    var profile = ResolveProfile(FindArg(args, "--profile"));
    var basePortVal = FindArg(args, "--base-port");
    var basePort = int.TryParse(basePortVal, out var bp) && bp >= 0 ? bp : (int?)null;
    var plan = Planner.Build(profile);
    if (basePort is { }) plan = ApplyBasePort(plan, basePort.Value);
    if (profile != Profile.Prod) plan = PortAllocator.AutoAvoidPorts(plan);

    if (format is "compose")
    {
        var exporter = new ComposeExporter();
        await exporter.GenerateAsync(plan, profile, outPath);
        Console.WriteLine($"compose exported -> {outPath}");
    // Surface port conflicts after export for quick feedback
    var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));
    if (conflicts.Count > 0) Console.WriteLine($"ports in use: {string.Join(", ", conflicts)}");
        return 0;
    }
    Console.Error.WriteLine($"Unknown export format: {format}");
    return 2;
}

static async Task<int> DoctorAsync(string[] args)
{
    var json = HasFlag(args, "--json");
    var engineArg = FindArg(args, "--engine");
    var provider = await SelectProviderAsync(engineArg);
    var avail = await provider.IsAvailableAsync();
    var engine = provider.EngineInfo();
    var effectiveOrder = string.Join(", ", ResolveProviderOrder());
    if (json)
    {
        var payload = new { provider = provider.Id, available = avail.Ok, reason = avail.Reason, engine = engine, order = effectiveOrder };
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return avail.Ok ? 0 : 3;
    }
    Console.WriteLine("Doctor checks:");
    Console.WriteLine($"- Provider order: {effectiveOrder}");
    Console.WriteLine("- Compose exporter: OK");
    Console.WriteLine($"- {provider.Id}: {(avail.Ok ? "OK" : "NOT AVAILABLE")} {(string.IsNullOrWhiteSpace(avail.Reason) ? string.Empty : "- " + RedactText(avail.Reason))}");
    if (!string.IsNullOrWhiteSpace(engine.Version)) Console.WriteLine(RedactText($"- Engine: {engine.Name} {engine.Version} ({engine.Endpoint})"));
    return avail.Ok ? 0 : 3;
}

static async Task<int> UpAsync(string[] args)
{
    var outPath = FindArg(args, "--file") ?? Constants.DefaultComposePath;
    var explain = HasFlag(args, "--explain");
    var dryRun = HasFlag(args, "--dry-run");
    var verbose = Verbosity(args) + (HasFlag(args, "--trace") ? 2 : 0) - (HasFlag(args, "--quiet") ? 1 : 0);
    var engineArg = FindArg(args, "--engine");
    var profile = ResolveProfile(FindArg(args, "--profile"));
    var timeoutSeconds = FindArg(args, "--timeout");
    var timeout = int.TryParse(timeoutSeconds, out var ts) && ts > 0 ? TimeSpan.FromSeconds(ts) : TimeSpan.FromSeconds(60);
    var basePortVal = FindArg(args, "--base-port");
    var basePort = int.TryParse(basePortVal, out var bp) && bp >= 0 ? bp : (int?)null;
    var conflictsArg = FindArg(args, "--conflicts")?.ToLowerInvariant(); // non-prod only: warn|fail
    var conflictsMode = conflictsArg is "fail" ? "fail" : conflictsArg is "warn" ? "warn" : null;

    var plan = Planner.Build(profile);
    if (basePort is { }) plan = ApplyBasePort(plan, basePort.Value);
    if (profile == Profile.Staging || profile == Profile.Prod)
    {
        Console.Error.WriteLine($"up is disabled for profile '{profile.ToString().ToLowerInvariant()}'; use 'sora export compose' to generate artifacts for this environment.");
        return 2;
    }
    // Detect port conflicts early and, in non-prod default mode, skip conflicting services (warn) instead of failing
    var initialConflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));
    List<string> skipped = new();
    if (profile != Profile.Prod && initialConflicts.Count > 0 && conflictsMode != "fail")
    {
        var newPlan = Planner.ApplyPortConflictSkip(plan, profile, out skipped);
        if (skipped.Count > 0)
        {
            Console.WriteLine($"warning: ports in use on host — skipping services: {string.Join(", ", skipped)} (ports: {string.Join(", ", initialConflicts)})");
            plan = newPlan;
        }
    }

    var exporter = new ComposeExporter();
    var outDir = Path.GetDirectoryName(outPath);
    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
    await exporter.GenerateAsync(plan, profile, outPath);

    var provider = await SelectProviderAsync(engineArg);
    // Detect port conflicts once; advisory on non-prod, fatal on prod
    var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));
    if (explain || verbose > 0)
    {
        var ei = provider.EngineInfo();
        Console.WriteLine($"provider: {provider.Id} | engine: {ei.Name} {ei.Version} | file: {outPath}");
        Console.WriteLine($"services: {plan.Services.Count}");
    Console.WriteLine($"profile: {profile} | timeout: {timeout.TotalSeconds:n0}s{(basePort is { } ? $" | base-port: {basePort}" : string.Empty)}{(conflictsMode is { } ? $" | conflicts: {conflictsMode}" : string.Empty)}");
    if (conflicts.Count > 0) Console.WriteLine($"ports in use: {string.Join(", ", conflicts)}");
    if (skipped.Count > 0) Console.WriteLine($"skipped services: {string.Join(", ", skipped)}");
    }
    if (dryRun) return 0;

    // Fail-fast in prod when conflicts exist (policy); non-prod can opt into fail via --conflicts fail
    if (profile == Profile.Prod && conflicts.Count > 0)
    {
        Console.Error.WriteLine($"port conflicts detected (prod): {string.Join(", ", conflicts)}");
        return 4;
    }
    if (profile != Profile.Prod && conflictsMode == "fail" && conflicts.Count > 0)
    {
        Console.Error.WriteLine($"port conflicts detected: {string.Join(", ", conflicts)}");
        return 4;
    }
    // Non-prod default: warn and continue when ports are occupied by host services
    if (profile != Profile.Prod && conflicts.Count > 0 && conflictsMode != "fail")
    {
        Console.WriteLine($"warning: ports in use on host — continuing: {string.Join(", ", conflicts)}");
    }

    try
    {
        await provider.Up(outPath, profile, new RunOptions(Detach: true, ReadinessTimeout: timeout));
        return 0;
    }
    catch (TimeoutException tex)
    {
        Console.Error.WriteLine($"readiness timeout: {tex.Message}");
        return 4;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"up failed: {ex.Message}");
        return 4;
    }
}

static async Task<int> DownAsync(string[] args)
{
    var outPath = FindArg(args, "--file") ?? Constants.DefaultComposePath;
    // Support --volumes (primary) and --prune-data (alias)
    var removeVolumes = HasFlag(args, "--volumes") || HasFlag(args, "--prune-data");
    var engineArg = FindArg(args, "--engine");
    var provider = await SelectProviderAsync(engineArg);
    await provider.Down(outPath, new StopOptions(RemoveVolumes: removeVolumes));
    return 0;
}

static async Task<int> StatusAsync(string[] args)
{
    var json = HasFlag(args, "--json");
    var engineArg = FindArg(args, "--engine");
    var profile = ResolveProfile(FindArg(args, "--profile"));
    var basePortVal = FindArg(args, "--base-port");
    var basePort = int.TryParse(basePortVal, out var bp) && bp >= 0 ? bp : (int?)null;
    var provider = await SelectProviderAsync(engineArg);
    var status = await provider.Status(new StatusOptions(Service: null));
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(status));
    }
    else
    {
        Console.WriteLine($"provider: {status.Provider} | engine: {status.EngineVersion}");
        foreach (var s in status.Services)
        {
            var health = s.Health is null ? string.Empty : $" ({s.Health})";
            Console.WriteLine($"- {s.Service}: {s.State}{health}");
        }
    // Live ports (provider runtime)
        var live = await provider.LivePorts();
        if (live.Count > 0)
        {
            Console.WriteLine("endpoints (live):");
            foreach (var g in live.GroupBy(p => p.Service))
            {
                var list = string.Join(", ", g.Select(p => EndpointFormatter.FormatLiveEndpoint(p)));
        Console.WriteLine($"  => {g.Key}: {list}");
            }
        }
        // Endpoint hints (plan-derived, provider-agnostic): scheme://host:port for each service with mapped ports
    var plan = Planner.Build(profile);
    if (basePort is { }) plan = ApplyBasePort(plan, basePort.Value);
    if (profile != Profile.Prod) plan = PortAllocator.AutoAvoidPorts(plan);
        if (plan.Services.Count > 0)
        {
            Console.WriteLine("endpoints (hints):");
            foreach (var svc in plan.Services)
            {
                if (svc.Ports is null || svc.Ports.Count == 0) continue;
        var list = string.Join(", ", svc.Ports.Select(p => Sora.Orchestration.Cli.Formatting.EndpointFormatter
            .GetPlanHint(svc.Image, p.Item2, p.Item1)));
                Console.WriteLine($"  -> {svc.Id}: {list}");
            }
            var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));
            if (conflicts.Count > 0) Console.WriteLine($"ports in use: {string.Join(", ", conflicts)}");
        }
    }
    return 0;
}

static async Task<int> InspectAsync(string[] args)
{
    var json = HasFlag(args, "--json");
    var quiet = HasFlag(args, "--quiet") || Environment.GetEnvironmentVariable("SORA_NO_INSPECT") == "1";
    var engineArg = FindArg(args, "--engine");
    var profile = ResolveProfile(FindArg(args, "--profile"));
    var basePortVal = FindArg(args, "--base-port");
    var basePort = int.TryParse(basePortVal, out var bp) && bp >= 0 ? bp : (int?)null;

    // First line: help hint (always)
    Console.WriteLine("Use -h for help");
    if (quiet) return 0;

    // Detect project: descriptor or existing compose file
    var cwd = Directory.GetCurrentDirectory();
    var descriptor = Constants.OrchestrationDescriptorCandidates
        .Select(name => Path.Combine(cwd, name))
        .FirstOrDefault(File.Exists);
    var composePath = Path.Combine(cwd, Constants.DefaultComposePath.Replace('/', Path.DirectorySeparatorChar));
    var hasCsproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).Any();
    var csprojPath = hasCsproj ? Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault() : null;
    var projectDetected = descriptor is not null || File.Exists(composePath) || hasCsproj;
    if (!projectDetected)
    {
        if (json)
        {
            var payload = new { detected = false, cwd };
            Console.WriteLine(JsonSerializer.Serialize(payload));
        }
        else
        {
            Console.WriteLine("No Sora project detected here.");
        }
        return 2;
    }

    // Provider availability snapshot
    var providers = new List<IHostingProvider> { new DockerProvider(), new PodmanProvider() };
    var availability = new List<object>();
    foreach (var p in providers)
    {
        var (ok, reason) = await p.IsAvailableAsync();
        var info = p.EngineInfo();
        availability.Add(new { id = p.Id, available = ok, reason, engine = new { info.Name, info.Version, info.Endpoint } });
    }

    // Build plan-derived hints
    var plan = Planner.Build(profile);
    if (basePort is { }) plan = ApplyBasePort(plan, basePort.Value);
    if (profile != Profile.Prod) plan = PortAllocator.AutoAvoidPorts(plan);
    var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));

    // Project metadata
    var projectName = new DirectoryInfo(cwd).Name;
    var envPath = Path.Combine(cwd, ".env");
    var files = new List<string>();
    if (descriptor is not null) files.Add(Path.GetFileName(descriptor));
    if (File.Exists(composePath)) files.Add(Constants.DefaultComposePath);
    if (File.Exists(envPath)) files.Add(".env");
    if (hasCsproj && csprojPath is not null) files.Add(Path.GetFileName(csprojPath));

    // Infer dependencies (best-effort, read-only): DB, Vector DB, AI provider(s), Auth
    var deps = InferDependencies(plan, cwd, csprojPath);
    var composeServices = DetectComposeServices(cwd);

    if (json)
    {
        var payload = new
        {
            detected = true,
            cwd,
            project = new { name = projectName, profile = profile.ToString(), compose = Constants.DefaultComposePath },
            providers = availability,
            services = plan.Services.Select(s => new
            {
                id = s.Id,
                image = s.Image,
                ports = s.Ports.Select(p => new { host = p.Host, container = p.Container }),
                health = s.Health is not null
            }),
            files,
            conflicts,
            composeServices,
            dependencies = deps
        };
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return 0;
    }

    // Human card
    Console.WriteLine($"CLI: Sora | profile: {profile}");
    Console.WriteLine($"Project: {projectName} ({cwd})");
    Console.WriteLine($"Files: {(files.Count == 0 ? "(none)" : string.Join(", ", files))}");
    Console.WriteLine("Providers:");
    foreach (dynamic p in availability)
    {
        var id = (string)p.GetType().GetProperty("id")!.GetValue(p)!;
        var available = (bool)p.GetType().GetProperty("available")!.GetValue(p)!;
        var eng = p.GetType().GetProperty("engine")!.GetValue(p)!;
        var engName = (string?)eng.GetType().GetProperty("Name")!.GetValue(eng) ?? string.Empty;
        var engVer = (string?)eng.GetType().GetProperty("Version")!.GetValue(eng) ?? string.Empty;
        Console.WriteLine($"- {id}: {(available ? "OK" : "NOT AVAILABLE")} {(string.IsNullOrWhiteSpace(engVer) ? string.Empty : $"- {engName} {engVer}")}");
    }
    Console.WriteLine($"Services: {plan.Services.Count}");
    foreach (var s in plan.Services)
    {
        var ports = s.Ports is null || s.Ports.Count == 0 ? "-" : string.Join(", ", s.Ports.Select(p => $"{p.Host}:{p.Container}"));
        var health = s.Health is null ? "no" : "yes";
        Console.WriteLine($"  -> {s.Id}: ports [{ports}], health: {health}");
    }
    if (composeServices is { Count: > 0 })
    {
        Console.WriteLine($"Compose services: {composeServices.Count}");
        foreach (var n in composeServices)
            Console.WriteLine($"  => {n}");
    }
    // Dependencies summary
    if (deps is not null)
    {
        var db = deps.TryGetValue("database", out var dbVal) ? dbVal : null;
        var vect = deps.TryGetValue("vector", out var veVal) ? veVal : null;
        var ai = deps.TryGetValue("ai", out var aiVal) ? aiVal : null;
        var auth = deps.TryGetValue("auth", out var auVal) ? auVal : null;
        Console.WriteLine("Dependencies:");
        if (db is not null) Console.WriteLine($"  db: {db}");
        if (vect is not null) Console.WriteLine($"  vector: {vect}");
        if (ai is not null) Console.WriteLine($"  ai: {ai}");
        if (auth is not null) Console.WriteLine($"  auth: {auth}");
    }
    if (conflicts.Count > 0) Console.WriteLine($"ports in use: {string.Join(", ", conflicts)}");

    // One-liners (easy to copy)
    Console.WriteLine("Next steps:");
    Console.WriteLine("  Sora up");
    Console.WriteLine("  Sora status");
    // Pick a likely service for logs if available, otherwise show placeholder
    var svcHint = plan.Services.Select(s => s.Id).FirstOrDefault(id => id.Equals("api", StringComparison.OrdinalIgnoreCase))
                 ?? plan.Services.Select(s => s.Id).FirstOrDefault()
                 ?? "[service]";
    Console.WriteLine($"  Sora logs --service {svcHint} --tail 100");
    Console.WriteLine("  Sora down --remove-orphans");
    Console.WriteLine("  Sora export compose");
    Console.WriteLine("  Sora doctor");
    Console.WriteLine("  Sora status --json");

    return 0;
}

// Heuristics to infer dependencies from plan, compose files, and project references
static Dictionary<string, string>? InferDependencies(Plan plan, string cwd, string? csprojPath)
{
    string? db = null, vector = null, ai = null, auth = null;

    // Compose-derived hints (highest precedence)
    bool cMongo = false, cPostgres = false, cWeaviate = false, cOllama = false;
    foreach (var rel in Constants.ComposeProbeCandidates)
    {
        var path = Path.Combine(cwd, rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) continue;
        try
        {
            var text = File.ReadAllText(path).ToLowerInvariant();
        if (text.Contains("image: postgres")) cPostgres = true;
        if (text.Contains("image: mongo")) cMongo = true;
        if (Regex.IsMatch(text, @"connectionstring:\s*""?mongodb://")) cMongo = true;
        if (text.Contains("weaviate")) cWeaviate = true;
        if (text.Contains("ollama")) cOllama = true;
        }
        catch { /* best-effort */ }
    }

    // Inspect project references for auth/web/data providers (second precedence)
    if (!string.IsNullOrWhiteSpace(csprojPath) && File.Exists(csprojPath))
    {
        try
        {
            var xml = File.ReadAllText(csprojPath);
            var lower = xml.ToLowerInvariant();
        if (lower.Contains("sora.web.auth")) auth = "enabled";
        if (lower.Contains("sora.data.weaviate")) vector ??= "weaviate";
        if (lower.Contains("sora.data.mongo")) db ??= "mongodb";
        }
        catch { /* ignore */ }
    }

    // Plan-derived hints (lowest precedence)
    var images = plan.Services.Select(s => s.Image?.ToLowerInvariant() ?? string.Empty).ToList();
    if (images.Any(i => i.Contains("postgres"))) db ??= "postgres";
    if (images.Any(i => i.Contains("mongo"))) db ??= "mongodb";
    if (images.Any(i => i.Contains("redis"))) db ??= "redis";
    if (images.Any(i => i.Contains("weaviate"))) vector ??= "weaviate";
    if (images.Any(i => i.Contains("qdrant"))) vector ??= "qdrant";
    if (images.Any(i => i.Contains("pinecone"))) vector ??= "pinecone";
    if (images.Any(i => i.Contains("ollama"))) ai ??= "ollama";

    // Apply compose overrides last (compose wins if present)
    if (cMongo) db = "mongodb"; else if (cPostgres) db = db ?? "postgres";
    if (cWeaviate) vector = "weaviate";
    if (cOllama) ai = "ollama";

    var result = new Dictionary<string, string>();
    if (db is not null) result["database"] = db;
    if (vector is not null) result["vector"] = vector;
    if (ai is not null) result["ai"] = ai;
    if (auth is not null) result["auth"] = auth;
    return result.Count == 0 ? null : result;
}

// Returns distinct service ids discovered in known compose files (best-effort)
static List<string>? DetectComposeServices(string cwd)
{
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var rel in Constants.ComposeProbeCandidates)
    {
        var path = Path.Combine(cwd, rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) continue;
        try
        {
            var text = File.ReadAllText(path);
            // Very small heuristic: capture lines under services: with 2-space indent like "  <name>:"
            var idx = text.IndexOf("services:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var block = text[(idx + 9)..];
            foreach (var raw in block.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!raw.StartsWith("  "))
                {
                    // Reached next top-level key
                    if (!raw.StartsWith(" ")) break;
                    continue;
                }
                var m = Regex.Match(raw, @"^\x20\x20([A-Za-z0-9._-]+):\s*$");
                if (m.Success)
                {
                    set.Add(m.Groups[1].Value);
                }
            }
        }
        catch { /* ignore */ }
    }
    return set.Count == 0 ? null : set.OrderBy(s => s).ToList();
}

static async Task<int> LogsAsync(string[] args)
{
    var follow = HasFlag(args, "-f") || HasFlag(args, "--follow");
    var tailVal = FindArg(args, "--tail");
    int? tail = int.TryParse(tailVal, out var t) ? t : null;
    var svc = FindArg(args, "--service");
    var since = FindArg(args, "--since");
    var engineArg = FindArg(args, "--engine");
    var provider = await SelectProviderAsync(engineArg);
    await foreach (var line in provider.Logs(new LogsOptions(Service: svc, Follow: follow, Tail: tail, Since: since)))
        Console.WriteLine(RedactText(line));
    return 0;
}

static string? FindArg(string[] args, string name)
{
    for (int i = 0; i < args.Length; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            return args[i + 1];
    return null;
}

static bool HasFlag(string[] args, string name)
    => Array.Exists(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

static int Verbosity(string[] args)
{
    int v = 0; foreach (var a in args) if (a is "-v") v++; else if (a is "-vv") v += 2; return v;
}

static Plan ApplyBasePort(Plan plan, int basePort)
{
    IReadOnlyList<ServiceSpec> Transform()
        => plan.Services.Select(s =>
        {
            var ports = s.Ports.Select(p => (Host: basePort + p.Host, Container: p.Container)).ToList();
            return s with { Ports = ports };
        }).ToList();
    return new Plan(plan.Profile, Transform());
}

// AutoAvoidPorts moved to Planning.PortAllocator for testability

// default plan moved into Planner.Build(profile)

static async Task<IHostingProvider> SelectProviderAsync(string? engine = null)
{
    // Windows-first default order; env var can override preference
    var providers = new List<IHostingProvider> { new DockerProvider(), new PodmanProvider() };
    if (!string.IsNullOrWhiteSpace(engine))
    {
        var forced = providers.FirstOrDefault(p => string.Equals(p.Id, engine, StringComparison.OrdinalIgnoreCase));
        if (forced is not null)
        {
            var (ok, _) = await forced.IsAvailableAsync();
            if (ok) return forced;
        }
        // If forced engine not available, fall through to auto-selection.
    }
    foreach (var p in OrderByPreference(providers))
    {
        var (ok, _) = await p.IsAvailableAsync();
        if (ok) return p;
    }
    // Fallback to docker instance to allow explain/dry-run even when engine missing
    return new DockerProvider();
}

static IEnumerable<IHostingProvider> OrderByPreference(IEnumerable<IHostingProvider> providers)
{
    var order = ResolveProviderOrder();
    return providers.OrderBy(p => Array.IndexOf(order, p.Id.ToLowerInvariant()));
}

static string[] ResolveProviderOrder()
{
    var env = Environment.GetEnvironmentVariable(Constants.EnvPreferredProviders);
    if (string.IsNullOrWhiteSpace(env)) return Constants.DefaultProviderOrder;
    var list = env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(s => s.ToLowerInvariant())
                  .ToArray();
    return list.Length == 0 ? Constants.DefaultProviderOrder : list;
}

static Profile ResolveProfile(string? arg)
{
    // precedence: --profile > SORA_ENV env var > Local
    var src = arg ?? Environment.GetEnvironmentVariable("SORA_ENV");
    return src?.ToLowerInvariant() switch
    {
        "ci" => Profile.Ci,
        "staging" => Profile.Staging,
        "prod" or "production" => Profile.Prod,
        _ => Profile.Local
    };
}
