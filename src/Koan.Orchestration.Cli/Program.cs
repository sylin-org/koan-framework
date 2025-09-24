using Koan.Orchestration;
using Koan.Orchestration.Cli;
using Koan.Orchestration.Cli.Formatting;
using Koan.Orchestration.Cli.Planning;
using Koan.Orchestration.Provider.Docker;
using Koan.Orchestration.Provider.Podman;
using Koan.Orchestration.Renderers.Compose;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Koan.Orchestration.Abstractions;
using Koan.Orchestration.Attributes;
using Koan.Orchestration.Cli.Infrastructure;
using Koan.Orchestration.Infrastructure;
using Koan.Orchestration.Models;
using static Koan.Orchestration.Redaction;

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
    "up" => await UpAsync(rest),
    "down" => await DownAsync(rest),
    "status" => await StatusAsync(rest),
    "logs" => await LogsAsync(rest),
    "inspect" => await InspectAsync(rest),
    _ => Help()
};

static int Help()
{
    Console.WriteLine("Koan <command> [options]\nCommands: export, doctor, up, down, status, logs, inspect");
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
    // Heuristics removed (ARCH-0049). Without a resolver, default to tcp.
    return "tcp";
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
    var portOverrideVal = FindArg(args, "--port");
    var portOverride = int.TryParse(portOverrideVal, out var pov) && pov > 0 ? pov : (int?)null;
    var exposeInternals = HasFlag(args, "--expose-internals");
    var noPersist = HasFlag(args, "--no-launch-manifest");
    var plan = Planner.Build(profile);
    if (basePort is { }) plan = ApplyBasePort(plan, basePort.Value);
    if (profile != Profile.Prod)
    {
        plan = Planner.AssignAppPublicPort(plan, portOverride, exposeInternals, persist: !noPersist);
        plan = PortAllocator.AutoAvoidPorts(plan);
    }

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
        Console.WriteLine(JsonConvert.SerializeObject(payload));
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
    var portOverrideVal = FindArg(args, "--port");
    var portOverride = int.TryParse(portOverrideVal, out var pov) && pov > 0 ? pov : (int?)null;
    var exposeInternals = HasFlag(args, "--expose-internals");
    var noPersist = HasFlag(args, "--no-launch-manifest");
    var conflictsArg = FindArg(args, "--conflicts")?.ToLowerInvariant(); // non-prod only: warn|fail
    var conflictsMode = conflictsArg is "fail" ? "fail" : conflictsArg is "warn" ? "warn" : null;

    var plan = Planner.Build(profile);
    if (basePort is { }) plan = ApplyBasePort(plan, basePort.Value);
    if (profile != Profile.Prod)
    {
        plan = Planner.AssignAppPublicPort(plan, portOverride, exposeInternals, persist: !noPersist);
    }
    if (profile == Profile.Staging || profile == Profile.Prod)
    {
        Console.Error.WriteLine($"up is disabled for profile '{profile.ToString().ToLowerInvariant()}'; use 'Koan export compose' to generate artifacts for this environment.");
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
            Console.WriteLine($"warning: ports in use on host - skipping services: {string.Join(", ", skipped)} (ports: {string.Join(", ", initialConflicts)})");
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
        Console.WriteLine($"networks: internal={OrchestrationConstants.InternalNetwork}, external={OrchestrationConstants.ExternalNetwork}");
        var appSvc = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
        if (appSvc is not null && appSvc.Ports.Count > 0)
        {
            var appPorts = string.Join(", ", appSvc.Ports.Select(p => $"{p.Host}:{p.Container}"));
            var src = Koan.Orchestration.Cli.Planning.Planner.LastPortAssignments.TryGetValue(appSvc.Id, out var a)
                ? a.Source
                : "unknown";
            Console.WriteLine($"app: {appSvc.Id} @ {appPorts} (source: {src})");
        }
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
        Console.WriteLine($"warning: ports in use on host - continuing: {string.Join(", ", conflicts)}");
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
    var portOverrideVal = FindArg(args, "--port");
    var portOverride = int.TryParse(portOverrideVal, out var pov) && pov > 0 ? pov : (int?)null;
    var exposeInternals = HasFlag(args, "--expose-internals");
    var provider = await SelectProviderAsync(engineArg);
    var noPersist = HasFlag(args, "--no-launch-manifest");
    var status = await provider.Status(new StatusOptions(Service: null));
    if (json)
    {
        Console.WriteLine(JsonConvert.SerializeObject(status));
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
        if (profile != Profile.Prod)
        {
            plan = Planner.AssignAppPublicPort(plan, portOverride, exposeInternals, persist: !noPersist);
            plan = PortAllocator.AutoAvoidPorts(plan);
        }
        if (plan.Services.Count > 0)
        {
            Console.WriteLine($"networks: internal={OrchestrationConstants.InternalNetwork}, external={OrchestrationConstants.ExternalNetwork}");
            var appSvc = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
            if (appSvc is not null && appSvc.Ports.Count > 0)
            {
                var appPorts = string.Join(", ", appSvc.Ports.Select(p => $"{p.Host}:{p.Container}"));
                var src = Koan.Orchestration.Cli.Planning.Planner.LastPortAssignments.TryGetValue(appSvc.Id, out var a)
                    ? a.Source
                    : "unknown";
                Console.WriteLine($"app: {appSvc.Id} @ {appPorts} (source: {src})");
            }
            Console.WriteLine("endpoints (hints):");
            foreach (var svc in plan.Services)
            {
                if (svc.Ports is null || svc.Ports.Count == 0) continue;
                var list = string.Join(", ", svc.Ports.Select(p => Koan.Orchestration.Cli.Formatting.EndpointFormatter
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
    var quiet = HasFlag(args, "--quiet") || Environment.GetEnvironmentVariable("Koan_NO_INSPECT") == "1";
    var engineArg = FindArg(args, "--engine");
    var profile = ResolveProfile(FindArg(args, "--profile"));
    var basePortVal = FindArg(args, "--base-port");
    var basePort = int.TryParse(basePortVal, out var bp) && bp >= 0 ? bp : (int?)null;
    var portOverrideVal = FindArg(args, "--port");
    var portOverride = int.TryParse(portOverrideVal, out var pov) && pov > 0 ? pov : (int?)null;
    var exposeInternals = HasFlag(args, "--expose-internals");

    // First line: help hint (human mode only)
    if (!json && !quiet) Console.WriteLine("Use -h for help");
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
            var detectedPayload = new { detected = false, cwd };
            Console.WriteLine(JsonConvert.SerializeObject(detectedPayload));
        }
        else
        {
            Console.WriteLine("No Koan project detected here.");
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
    if (profile != Profile.Prod)
    {
        plan = Planner.AssignAppPublicPort(plan, portOverride, exposeInternals);
        plan = PortAllocator.AutoAvoidPorts(plan);
    }
    var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));

    // Project metadata
    var projectName = new DirectoryInfo(cwd).Name;
    var envPath = Path.Combine(cwd, ".env");
    var files = new List<string>();
    if (descriptor is not null) files.Add(Path.GetFileName(descriptor));
    if (File.Exists(composePath)) files.Add(Constants.DefaultComposePath);
    if (File.Exists(envPath)) files.Add(".env");
    if (hasCsproj && csprojPath is not null) files.Add(Path.GetFileName(csprojPath));

    // Dependencies (declarative, no heuristics): derive from plan ServiceType
    var deps = ComputeDependenciesFromPlan(plan);

    // Declared services via generated manifest (preferred over assembly reference scan)
    var referenced = Koan.Orchestration.Cli.Planning.ProjectDependencyAnalyzer.DiscoverServicesFromManifest();
    var manifestIdDups = Koan.Orchestration.Cli.Planning.ProjectDependencyAnalyzer.ManifestIdDuplicates;
    var manifestDetails = Koan.Orchestration.Cli.Planning.ProjectDependencyAnalyzer.DiscoverManifestServiceDetails();

    // Discover configured auth providers from appsettings.* (non-inferential)
    var authProviders = DiscoverAuthProviders(cwd);

    // If none configured, surface adapter capabilities from generated manifest (attributes)
    List<(string Id, string Name, string Protocol)>? authCapabilities = null;
    if (authProviders is null || authProviders.Count == 0)
    {
        try
        {
            // Triggers manifest scan and fills ProjectDependencyAnalyzer.ManifestAuthProviders
            _ = Koan.Orchestration.Cli.Planning.ProjectDependencyAnalyzer.DiscoverDraft(profile);
            var list = Koan.Orchestration.Cli.Planning.ProjectDependencyAnalyzer.ManifestAuthProviders;
            if (list is { Count: > 0 })
            {
                authCapabilities = list
                    .Select(p => (p.Id, p.Name, PrettyProtocol(p.Protocol)))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch { }
    }

    // App summary (ids/ports) for JSON payload + manifest-derived metadata (fallback by Kind/Type=App)
    var appSvcForJson = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
    var appIds = appSvcForJson is not null ? new[] { appSvcForJson.Id } : Array.Empty<string>();
    var appPorts = appSvcForJson is not null
        ? appSvcForJson.Ports.Select(p => new { host = p.Host, container = p.Container }).ToArray()
        : Array.Empty<object>();
    var appMdForJson = appSvcForJson is null
        ? null
        : (manifestDetails?.FirstOrDefault(m => m.Id.Equals(appSvcForJson.Id, StringComparison.OrdinalIgnoreCase))
           ?? manifestDetails?.FirstOrDefault(m => (m.Kind is int k && k == 0) || (m.Type is not null && m.Type == ServiceType.App)));

    if (json)
    {
        var payload = new
        {
            detected = true,
            project = new { name = projectName, profile = profile.ToString(), compose = Constants.DefaultComposePath },
            networks = new { internalName = OrchestrationConstants.InternalNetwork, externalName = OrchestrationConstants.ExternalNetwork },
            app = new
            {
                ids = appIds,
                ports = appPorts,
                manifestId = appMdForJson?.Id,
                name = appMdForJson?.Name,
                capabilities = appMdForJson?.Capabilities
            },
            providers = availability,
            duplicates = manifestIdDups,
            services = plan.Services.Select(s =>
            {
                var md = manifestDetails?.FirstOrDefault(m => m.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
                // If app id differs between plan and manifest, provide Kind/Type=App fallback for app service metadata
                if (md is null && s.Type == ServiceType.App)
                {
                    md = manifestDetails?.FirstOrDefault(m => (m.Kind is int k && k == 0) || (m.Type is not null && m.Type == ServiceType.App));
                }
                return new
                {
                    id = s.Id,
                    image = s.Image,
                    ports = s.Ports.Select(p => new { host = p.Host, container = p.Container }),
                    health = s.Health is not null,
                    type = s.Type switch
                    {
                        ServiceType.App => "app",
                        ServiceType.Database => "database",
                        ServiceType.Vector => "vector",
                        ServiceType.Ai => "ai",
                        _ => "service"
                    },
                    name = md?.Name,
                    // Unified manifest fields (when present on referenced list)
                    kind = referenced?.FirstOrDefault(r => r.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase)).Type switch
                    {
                        ServiceType.App => "App",
                        ServiceType.Database => "Database",
                        ServiceType.Vector => "Vector",
                        ServiceType.Ai => "Ai",
                        ServiceType.Service => "Other",
                        _ => (string?)null
                    },
                    qualifiedCode = md?.QualifiedCode,
                    containerImage = md?.ContainerImage,
                    defaultTag = md?.DefaultTag,
                    defaultPorts = md?.DefaultPorts,
                    healthEndpoint = md?.HealthEndpoint,
                    provides = md?.Provides,
                    consumes = md?.Consumes,
                    capabilities = md?.Capabilities ?? (md?.Provides is { Count: > 0 } ? md.Provides.ToDictionary(k => k, v => (string?)string.Empty) : null)
                };
            }),
            auth = authProviders is null && authCapabilities is null ? null : new
            {
                count = authProviders?.Count ?? 0,
                providers = authProviders?.Select(p => new { id = p.Id, name = p.Name, protocol = p.Protocol }),
                capabilities = authCapabilities?.Select(p => new { id = p.Id, name = p.Name, protocol = p.Protocol })
            },
            files,
            conflicts,
            dependencies = deps,
            referenced = referenced?.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                type = r.Type switch
                {
                    ServiceType.App => "app",
                    ServiceType.Database => "database",
                    ServiceType.Vector => "vector",
                    ServiceType.Ai => "ai",
                    _ => r.Type?.ToString()?.ToLowerInvariant() ?? "service"
                }
            })
        };
        Console.WriteLine(JsonConvert.SerializeObject(payload));
        return 0;
    }

    // Human card - Enhanced Aligned Columns format
    Console.WriteLine($"== Koan Context: {projectName} ==");
    Console.WriteLine($"Profile: {profile} | Path: {new DirectoryInfo(cwd).Name}");
    Console.WriteLine();

    // Providers section
    Console.WriteLine("PROVIDERS     STATUS    VERSION");
    foreach (dynamic p in availability)
    {
        var id = (string)p.GetType().GetProperty("id")!.GetValue(p)!;
        var available = (bool)p.GetType().GetProperty("available")!.GetValue(p)!;
        var eng = p.GetType().GetProperty("engine")!.GetValue(p)!;
        var engVer = (string?)eng.GetType().GetProperty("Version")!.GetValue(eng) ?? string.Empty;
        var status = available ? "OK" : "FAIL";
        Console.WriteLine($"{id,-13} {status,-9} {engVer}");
    }
    Console.WriteLine();

    // Application section
    var appSvc = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
    if (appSvc is not null && appSvc.Ports.Count > 0)
    {
        var appPortsText = string.Join(", ", appSvc.Ports.Select(p => $"{p.Host}:{p.Container}"));
        var src = Koan.Orchestration.Cli.Planning.Planner.LastPortAssignments.TryGetValue(appSvc.Id, out var a)
            ? a.Source
            : "unknown";
        // Resolve manifest details for DX-meaningful extras
        // Match by service id first; if ids differ (e.g., manifest shortCode vs. plan 'api'), fall back to Kind/Type == App
        var appMd = manifestDetails?.FirstOrDefault(m => m.Id.Equals(appSvc.Id, StringComparison.OrdinalIgnoreCase))
               ?? manifestDetails?.FirstOrDefault(m => (m.Kind is int k && k == 0) || (m.Type is not null && m.Type == ServiceType.App));
        Console.WriteLine("APPLICATION   PORT          SOURCE        NETWORKS");
        Console.WriteLine($"{appSvc.Id,-13} {appPortsText,-13} {src,-13} {OrchestrationConstants.InternalNetwork} + {OrchestrationConstants.ExternalNetwork}");

        // Optional: show friendly name and qualified code if present
        if (!string.IsNullOrWhiteSpace(appMd?.Name) && !appMd!.Name!.Equals(appSvc.Id, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{"",-13} {"NAME",-13} {appMd!.Name}");
        }
        if (!string.IsNullOrWhiteSpace(appMd?.QualifiedCode))
        {
            Console.WriteLine($"{"",-13} {"CODE",-13} {appMd!.QualifiedCode}");
        }

        // Capabilities: render compact list key[=value], capped with +N more for readability
        var appCaps = appMd?.Capabilities is { Count: > 0 } ? appMd.Capabilities : (appMd?.Provides is { Count: > 0 } ? appMd.Provides.ToDictionary(k => k, v => (string?)string.Empty) : null);
        if (appCaps is { Count: > 0 })
        {
            IEnumerable<string> Items()
            {
                foreach (var kv in appCaps!)
                {
                    var key = kv.Key;
                    var val = string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value;
                    yield return val is null ? key : ($"{key}={val}");
                }
            }
            var list = Items().ToList();
            const int max = 6;
            var shown = list.Take(max).ToList();
            var suffix = list.Count > max ? $"  +{list.Count - max} more" : string.Empty;
            Console.WriteLine($"{"",-13} {"CAPABILITIES",-13} {string.Join(", ", shown)}{suffix}");
        }
        Console.WriteLine();
    }

    // Services section
    Console.WriteLine("SERVICES      PORTS         HEALTH    TYPE        IMAGE:TAG");
    foreach (var s in plan.Services)
    {
        var md = manifestDetails?.FirstOrDefault(m => m.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
        var ports = s.Ports is null || s.Ports.Count == 0 ? "internal" :
                    s.Ports.Any(p => p.Host > 0) ? string.Join(", ", s.Ports.Where(p => p.Host > 0).Select(p => p.Host.ToString())) : "internal";
        var health = s.Health is null ? "-" : "OK";
        var type = s.Type switch
        {
            ServiceType.App => "app",
            ServiceType.Database => "database",
            ServiceType.Vector => "vector",
            ServiceType.Ai => "ai",
            _ => "service"
        };
        var imageTag = s.Image ?? ((md?.ContainerImage, md?.DefaultTag) is (string img, string tag) && !string.IsNullOrWhiteSpace(img)
            ? (string.IsNullOrWhiteSpace(tag) ? img : (img + ":" + tag)) : string.Empty);
        Console.WriteLine($"{s.Id,-13} {ports,-13} {health,-9} {type,-11} {imageTag}");
        // Optional detail lines: show NAME and CAPABILITIES when present
        if (!string.IsNullOrWhiteSpace(md?.Name) && !md!.Name!.Equals(s.Id, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{"",-13} {"NAME",-13} {md!.Name}");
        }
        var svcCaps = md?.Capabilities is { Count: > 0 } ? md.Capabilities : (md?.Provides is { Count: > 0 } ? md.Provides.ToDictionary(k => k, v => (string?)string.Empty) : null);
        if (svcCaps is { Count: > 0 })
        {
            IEnumerable<string> CapItems()
            {
                foreach (var kv in svcCaps!)
                {
                    var key = kv.Key;
                    var val = string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value;
                    yield return val is null ? key : ($"{key}={val}");
                }
            }
            var caps = CapItems().ToList();
            const int maxCaps = 6;
            var shownCaps = caps.Take(maxCaps).ToList();
            var suffixCaps = caps.Count > maxCaps ? $"  +{caps.Count - maxCaps} more" : string.Empty;
            Console.WriteLine($"{"",-13} {"CAPABILITIES",-13} {string.Join(", ", shownCaps)}{suffixCaps}");
        }
    }
    Console.WriteLine();

    // Auth: show configured providers and capabilities (both when present)
    if (authProviders is { Count: > 0 })
    {
        // Align with SERVICES columns: [SERVICES(13)] [PORTS(13)] [HEALTH(9)]
        Console.WriteLine($"{"AUTH",-13} {"PROVIDERS",-13} {"PROTOCOL",-9}");
        foreach (var ap in authProviders)
        {
            Console.WriteLine($"{"",-13} {ap.Name,-13} {ap.Protocol,-9}");
        }
        Console.WriteLine();
    }
    if (authCapabilities is { Count: > 0 })
    {
        // Align with SERVICES columns: [SERVICES(13)] [PORTS(13)] [HEALTH(9)]
        Console.WriteLine($"{"AUTH CAPABILITIES",-27} {"PROTOCOL",-9}");
        foreach (var ap in authCapabilities)
        {
            Console.WriteLine($"  {ap.Name,-25} {ap.Protocol,-9}");
        }
        Console.WriteLine();
    }

    // Dependencies section
    if (deps is not null)
    {
        var depItems = new List<string>();
        if (deps.TryGetValue("database", out var dbVal) && dbVal is not null) depItems.Add(dbVal);
        if (deps.TryGetValue("vector", out var veVal) && veVal is not null) depItems.Add(veVal);
        if (deps.TryGetValue("ai", out var aiVal) && aiVal is not null) depItems.Add(aiVal);
        if (authProviders is { Count: > 0 }) depItems.Add($"auth:{authProviders.Count}");
        else if (deps.TryGetValue("auth", out var auVal) && auVal is not null) depItems.Add("auth-enabled");
        if (depItems.Count > 0)
        {
            Console.WriteLine($"DEPENDENCIES  {string.Join(" | ", depItems)}");
        }
    }
    if (manifestIdDups is { Count: > 0 })
    {
        Console.WriteLine();
        Console.WriteLine("DUPLICATE IDS");
        Console.WriteLine(string.Join(", ", manifestIdDups));
    }

    if (conflicts.Count > 0)
    {
        Console.WriteLine($"CONFLICTS     {string.Join(", ", conflicts)}");
    }
    if (referenced is { Count: > 0 })
    {
        Console.WriteLine();
        Console.WriteLine("DECLARED SERVICES (from manifest)");
        foreach (var s in referenced)
        {
            var type = s.Type switch
            {
                ServiceType.App => "app",
                ServiceType.Database => "database",
                ServiceType.Vector => "vector",
                ServiceType.Ai => "ai",
                _ => s.Type?.ToString()?.ToLowerInvariant() ?? "service"
            };
            var md = manifestDetails?.FirstOrDefault(m => m.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
            var name = string.IsNullOrWhiteSpace(s.Name) ? (md?.Name ?? s.Id) : s.Name!;
            var extra = new List<string>();
            if (!string.IsNullOrWhiteSpace(md?.QualifiedCode)) extra.Add(md!.QualifiedCode!);
            if (!string.IsNullOrWhiteSpace(md?.ContainerImage)) extra.Add(md!.ContainerImage + (string.IsNullOrWhiteSpace(md!.DefaultTag) ? string.Empty : ":" + md!.DefaultTag));
            if (md?.DefaultPorts is { Length: > 0 }) extra.Add("ports:" + string.Join(',', md!.DefaultPorts!));
            if (!string.IsNullOrWhiteSpace(md?.HealthEndpoint)) extra.Add("health:" + md!.HealthEndpoint);
            Console.WriteLine($"{s.Id}  ({type}) - {name}{(extra.Count > 0 ? " | " + string.Join(" | ", extra) : string.Empty)}");
        }
    }
    Console.WriteLine();

    // Commands section
    Console.WriteLine("COMMANDS");
    Console.WriteLine("Koan up                  # Start all services");
    Console.WriteLine("Koan status              # Check service status");
    Console.WriteLine("Koan export compose      # Generate artifacts");
    // Pick a likely service for logs if available, otherwise show placeholder
    var svcHint = plan.Services.Select(s => s.Id).FirstOrDefault(id => id.Equals("api", StringComparison.OrdinalIgnoreCase))
                 ?? plan.Services.Select(s => s.Id).FirstOrDefault()
                 ?? "[service]";
    Console.WriteLine($"Koan logs --service {svcHint} --tail 100");

    return 0;
}

// Declarative dependencies: derive from plan ServiceType; no compose/csproj/image heuristics
static Dictionary<string, string>? ComputeDependenciesFromPlan(Plan plan)
{
    string? Pick(string kind)
        => plan.Services
            .FirstOrDefault(s => kind switch
            {
                "database" => s.Type == ServiceType.Database,
                "vector" => s.Type == ServiceType.Vector,
                "ai" => s.Type == ServiceType.Ai,
                _ => false
            })?.Id;

    var db = Pick("database");
    var vec = Pick("vector");
    var ai = Pick("ai");

    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(db)) dict["database"] = db!;
    if (!string.IsNullOrWhiteSpace(vec)) dict["vector"] = vec!;
    if (!string.IsNullOrWhiteSpace(ai)) dict["ai"] = ai!;
    return dict.Count == 0 ? null : dict;
}

// Read-only scan of appsettings.* for configured auth providers
static List<(string Id, string Name, string Protocol)>? DiscoverAuthProviders(string cwd)
{
    try
    {
        var files = new[] { "appsettings.json", "appsettings.Local.json", "appsettings.Development.json" }
            .Select(f => Path.Combine(cwd, f))
            .Where(File.Exists)
            .ToArray();
        if (files.Length == 0) return null;

        // Merge providers across files, latter override earlier
        var map = new Dictionary<string, (string? Name, string? Type)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files)
        {
            var root = JToken.Parse(File.ReadAllText(path));
            var providers = root?["Koan"]?["Web"]?["Auth"]?["Providers"] as JObject;
            if (providers is null) continue;
            foreach (var prop in providers.Properties())
            {
                var id = prop.Name;
                var obj = prop.Value as JObject;
                string? name = obj?["DisplayName"]?.Type == JTokenType.String ? (string?)obj?["DisplayName"] : null;
                string? type = obj?["Type"]?.Type == JTokenType.String ? (string?)obj?["Type"] : null;
                if (map.TryGetValue(id, out var existing))
                    map[id] = (name ?? existing.Name, type ?? existing.Type);
                else
                    map[id] = (name, type);
            }
        }
        if (map.Count == 0) return null;
        var list = new List<(string Id, string Name, string Protocol)>(map.Count);
        foreach (var (id, v) in map)
        {
            var name = string.IsNullOrWhiteSpace(v.Name) ? Titleize(id) : v.Name!;
            var protocol = PrettyProtocol(v.Type);
            list.Add((id, name, protocol));
        }
        // Stable order by name
        return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    catch { return null; }
}

static string PrettyProtocol(string? type)
    => string.IsNullOrWhiteSpace(type) ? "OIDC"
       : type!.ToLowerInvariant() switch
       {
           "oidc" => "OIDC",
           "oauth2" or "oauth" => "OAuth",
           "saml" => "SAML",
           "ldap" => "LDAP",
           _ => type
       };

static string Titleize(string id)
{
    if (string.IsNullOrWhiteSpace(id)) return id;
    var parts = id.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < parts.Length; i++)
    {
        var p = parts[i];
        parts[i] = char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1) : string.Empty);
    }
    return string.Join(' ', parts);
}

// Best-effort: infer auth adapter capabilities from project references (Google/Microsoft/Discord/OIDC)
// Attribute-based: read referenced assemblies to find AuthProviderDescriptorAttribute without loading runtime DI
// (intentionally removed) - SoC: capabilities are sourced from the generated manifest only

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
    // precedence: --profile > Koan_ENV env var > Local
    var src = arg ?? Environment.GetEnvironmentVariable("Koan_ENV");
    return src?.ToLowerInvariant() switch
    {
        "ci" => Profile.Ci,
        "staging" => Profile.Staging,
        "prod" or "production" => Profile.Prod,
        _ => Profile.Local
    };
}
