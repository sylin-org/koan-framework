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

// Minimal DX-first CLI: export, doctor, up, down, status, logs
// Flags: -v/-vv, --json (for explain/status), --dry-run, --explain

var (cmd, rest) = args.Length == 0
    ? ("help", Array.Empty<string>())
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
    _        => Help()
};

static int Help()
{
    Console.WriteLine("sora <command> [options]\nCommands: export, doctor, up, down, status, logs");
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
    var exporter = new ComposeExporter();
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
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
