using Koan.Orchestration;
using Koan.Orchestration.Abstractions;
using Koan.Orchestration.Attributes;
using Koan.Orchestration.Cli.Formatting;
using Koan.Orchestration.Cli.Infrastructure;
using Koan.Orchestration.Cli.Planning;
using Koan.Orchestration.Connector.Docker;
using Koan.Orchestration.Connector.Podman;
using Koan.Orchestration.Infrastructure;
using Koan.Orchestration.Models;
using Koan.Orchestration.Renderers.Connector.Compose;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Orchestration.Cli.Runtime;

internal sealed class CommandRuntime
{
    public CommandRuntime()
    {
        EndpointResolverBootstrapper.Register();
    }

    public Profile ResolveProfile(string? value)
        => value?.ToLowerInvariant() switch
        {
            "ci" => Profile.Ci,
            "staging" => Profile.Staging,
            "prod" or "production" => Profile.Prod,
            _ => Profile.Local
        };

    public Profile ResolveEffectiveProfile(string? optionValue)
        => ResolveProfile(optionValue ?? Environment.GetEnvironmentVariable("Koan_ENV"));

    public async Task<int> ExecuteExport(ExportCommandOptions options)
    {
        var profile = ResolveEffectiveProfile(options.Profile);
        var plan = BuildPlan(profile, options.BasePort, options.PortOverride, options.ExposeInternals, persistLaunchManifest: !options.NoLaunchManifest, autoAvoidPorts: profile != Profile.Prod);

        if (!string.Equals(options.Format, "compose", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown export format: {options.Format}");
            return 2;
        }

        var exporter = new ComposeExporter();
        await exporter.Generate(plan, profile, options.OutputPath);
        Console.WriteLine($"compose exported -> {options.OutputPath}");

        var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));
        if (conflicts.Count > 0)
        {
            Console.WriteLine($"ports in use: {string.Join(", ", conflicts)}");
        }

        return 0;
    }

    public async Task<int> ExecuteDoctor(DoctorCommandOptions options)
    {
        var provider = await SelectProvider(options.Engine);
        var availability = await provider.IsAvailable();
        var engineInfo = provider.EngineInfo();
        var order = string.Join(", ", ResolveProviderOrder());

        if (options.Json)
        {
            var payload = new { provider = provider.Id, available = availability.Ok, reason = availability.Reason, engine = engineInfo, order };
            Console.WriteLine(JsonConvert.SerializeObject(payload));
            return availability.Ok ? 0 : 3;
        }

        Console.WriteLine("Doctor checks:");
        Console.WriteLine($"- Provider order: {order}");
        Console.WriteLine("- Compose exporter: OK");
        Console.WriteLine($"- {provider.Id}: {(availability.Ok ? "OK" : "NOT AVAILABLE")} {(string.IsNullOrWhiteSpace(availability.Reason) ? string.Empty : "- " + Orchestration.Redaction.RedactText(availability.Reason))}");
        if (!string.IsNullOrWhiteSpace(engineInfo.Version))
        {
            Console.WriteLine(Orchestration.Redaction.RedactText($"- Engine: {engineInfo.Name} {engineInfo.Version} ({engineInfo.Endpoint})"));
        }

        return availability.Ok ? 0 : 3;
    }

    public async Task<int> ExecuteUp(UpCommandOptions options)
    {
        var profile = ResolveEffectiveProfile(options.Profile);
        var timeout = options.TimeoutSeconds.HasValue && options.TimeoutSeconds.Value > 0
            ? TimeSpan.FromSeconds(options.TimeoutSeconds.Value)
            : TimeSpan.FromSeconds(60);
        var plan = BuildPlan(profile, options.BasePort, options.PortOverride, options.ExposeInternals, persistLaunchManifest: !options.NoLaunchManifest, autoAvoidPorts: false);

        if (profile != Profile.Prod)
        {
            plan = PortAllocator.AutoAvoidPorts(plan);
        }

        if (profile == Profile.Staging || profile == Profile.Prod)
        {
            Console.Error.WriteLine($"up is disabled for profile '{profile.ToString().ToLowerInvariant()}'; use 'Koan export compose' to generate artifacts for this environment.");
            return 2;
        }

        var initialConflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));
        var skipped = new List<string>();
        if (profile != Profile.Prod && initialConflicts.Count > 0 && !string.Equals(options.ConflictsMode, "fail", StringComparison.OrdinalIgnoreCase))
        {
            var adjusted = Planner.ApplyPortConflictSkip(plan, profile, out skipped);
            if (skipped.Count > 0)
            {
                Console.WriteLine($"warning: ports in use on host - skipping services: {string.Join(", ", skipped)} (ports: {string.Join(", ", initialConflicts)})");
                plan = adjusted;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? string.Empty);
        var exporter = new ComposeExporter();
        await exporter.Generate(plan, profile, options.OutputPath);

        var provider = await SelectProvider(options.Engine);
        var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));

        var verbosity = (options.Verbose ? 1 : 0) + (options.Trace ? 2 : 0) - (options.Quiet ? 1 : 0);
        if (options.Explain || verbosity > 0)
        {
            var engineInfo = provider.EngineInfo();
            Console.WriteLine($"provider: {provider.Id} | engine: {engineInfo.Name} {engineInfo.Version} | file: {options.OutputPath}");
            Console.WriteLine($"services: {plan.Services.Count}");
            Console.WriteLine($"profile: {profile} | timeout: {timeout.TotalSeconds:n0}s{(options.BasePort.HasValue ? $" | base-port: {options.BasePort}" : string.Empty)}{(options.ConflictsMode is { } ? $" | conflicts: {options.ConflictsMode}" : string.Empty)}");
            Console.WriteLine($"networks: internal={OrchestrationConstants.InternalNetwork}, external={OrchestrationConstants.ExternalNetwork}");
            var appSvc = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
            if (appSvc is not null && appSvc.Ports.Count > 0)
            {
                var appPorts = string.Join(", ", appSvc.Ports.Select(p => $"{p.Host}:{p.Container}"));
                var src = Planner.LastPortAssignments.TryGetValue(appSvc.Id, out var alloc) ? alloc.Source : "unknown";
                Console.WriteLine($"app: {appSvc.Id} @ {appPorts} (source: {src})");
            }
            if (conflicts.Count > 0) Console.WriteLine($"ports in use: {string.Join(", ", conflicts)}");
            if (skipped.Count > 0) Console.WriteLine($"skipped services: {string.Join(", ", skipped)}");
        }

        if (options.DryRun) return 0;

        if (profile == Profile.Prod && conflicts.Count > 0)
        {
            Console.Error.WriteLine($"port conflicts detected (prod): {string.Join(", ", conflicts)}");
            return 4;
        }
        if (profile != Profile.Prod && string.Equals(options.ConflictsMode, "fail", StringComparison.OrdinalIgnoreCase) && conflicts.Count > 0)
        {
            Console.Error.WriteLine($"port conflicts detected: {string.Join(", ", conflicts)}");
            return 4;
        }
        if (profile != Profile.Prod && conflicts.Count > 0 && !string.Equals(options.ConflictsMode, "fail", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"warning: ports in use on host - continuing: {string.Join(", ", conflicts)}");
        }

        try
        {
            await provider.Up(options.OutputPath, profile, new RunOptions(Detach: true, ReadinessTimeout: timeout));
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

    public async Task<int> ExecuteDown(DownCommandOptions options)
    {
        var provider = await SelectProvider(options.Engine);
        await provider.Down(options.OutputPath ?? Constants.DefaultComposePath, new StopOptions(RemoveVolumes: options.RemoveVolumes));
        return 0;
    }

    public async Task<int> ExecuteStatus(StatusCommandOptions options)
    {
        var profile = ResolveEffectiveProfile(options.Profile);
        var provider = await SelectProvider(options.Engine);
        var status = await provider.Status(new StatusOptions(Service: null));

        if (options.Json)
        {
            Console.WriteLine(JsonConvert.SerializeObject(status));
            return 0;
        }

        Console.WriteLine($"provider: {status.Provider} | engine: {status.EngineVersion}");
        foreach (var s in status.Services)
        {
            var health = s.Health is null ? string.Empty : $" ({s.Health})";
            Console.WriteLine($"- {s.Service}: {s.State}{health}");
        }

        var live = await provider.LivePorts();
        if (live.Count > 0)
        {
            Console.WriteLine("endpoints (live):");
            foreach (var group in live.GroupBy(p => p.Service))
            {
                var list = string.Join(", ", group.Select(EndpointFormatter.FormatLiveEndpoint));
                Console.WriteLine($"  => {group.Key}: {list}");
            }
        }

        var plan = BuildPlan(profile, options.BasePort, options.PortOverride, options.ExposeInternals, persistLaunchManifest: !options.NoLaunchManifest, autoAvoidPorts: profile != Profile.Prod);
        if (plan.Services.Count > 0)
        {
            Console.WriteLine($"networks: internal={OrchestrationConstants.InternalNetwork}, external={OrchestrationConstants.ExternalNetwork}");
            var appSvc = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
            if (appSvc is not null && appSvc.Ports.Count > 0)
            {
                var appPorts = string.Join(", ", appSvc.Ports.Select(p => $"{p.Host}:{p.Container}"));
                var src = Planner.LastPortAssignments.TryGetValue(appSvc.Id, out var alloc) ? alloc.Source : "unknown";
                Console.WriteLine($"app: {appSvc.Id} @ {appPorts} (source: {src})");
            }
            Console.WriteLine("endpoints (hints):");
            foreach (var svc in plan.Services)
            {
                if (svc.Ports is null || svc.Ports.Count == 0) continue;
                var list = string.Join(", ", svc.Ports.Select(p => EndpointFormatter.GetPlanHint(svc.Image, p.Item2, p.Item1)));
                Console.WriteLine($"  -> {svc.Id}: {list}");
            }
            var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));
            if (conflicts.Count > 0) Console.WriteLine($"ports in use: {string.Join(", ", conflicts)}");
        }

        return 0;
    }

    public async Task<int> ExecuteInspect(InspectCommandOptions options)
    {
        if (options.Quiet) return 0;

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
            if (options.Json)
            {
                var payload = new { detected = false, cwd };
                Console.WriteLine(JsonConvert.SerializeObject(payload));
            }
            else
            {
                Console.WriteLine("No Koan project detected here.");
            }
            return 2;
        }

        var providers = new List<IHostingProvider> { new DockerProvider(), new PodmanProvider() };
        var availability = new List<object>();
        foreach (var p in providers)
        {
            var (ok, reason) = await p.IsAvailable();
            var info = p.EngineInfo();
            availability.Add(new { id = p.Id, available = ok, reason, engine = new { info.Name, info.Version, info.Endpoint } });
        }

        var profile = ResolveEffectiveProfile(options.Profile);
        var plan = BuildPlan(profile, options.BasePort, options.PortOverride, options.ExposeInternals, persistLaunchManifest: true, autoAvoidPorts: profile != Profile.Prod);
        var conflicts = Planner.FindConflictingPorts(plan.Services.SelectMany(s => s.Ports.Select(p => p.Host)));

        var projectName = new DirectoryInfo(cwd).Name;
        var envPath = Path.Combine(cwd, ".env");
        var files = new List<string>();
        if (descriptor is not null) files.Add(Path.GetFileName(descriptor));
        if (File.Exists(composePath)) files.Add(Constants.DefaultComposePath);
        if (File.Exists(envPath)) files.Add(".env");
        if (hasCsproj && csprojPath is not null) files.Add(Path.GetFileName(csprojPath));

        var deps = ComputeDependenciesFromPlan(plan);
        var referenced = ProjectDependencyAnalyzer.DiscoverServicesFromManifest();
        var manifestIdDups = ProjectDependencyAnalyzer.ManifestIdDuplicates;
        var manifestDetails = ProjectDependencyAnalyzer.DiscoverManifestServiceDetails();

        var authProviders = DiscoverAuthProviders(cwd);
        List<(string Id, string Name, string Protocol)>? authCapabilities = null;
        if (authProviders is null || authProviders.Count == 0)
        {
            try
            {
                _ = ProjectDependencyAnalyzer.DiscoverDraft(profile);
                var list = ProjectDependencyAnalyzer.ManifestAuthProviders;
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

        var appSvcForJson = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
        var appIds = appSvcForJson is not null ? new[] { appSvcForJson.Id } : Array.Empty<string>();
        var appPorts = appSvcForJson is not null
            ? appSvcForJson.Ports.Select(p => new { host = p.Host, container = p.Container }).ToArray()
            : Array.Empty<object>();
        var appMdForJson = appSvcForJson is null
            ? null
            : (manifestDetails?.FirstOrDefault(m => m.Id.Equals(appSvcForJson.Id, StringComparison.OrdinalIgnoreCase))
               ?? manifestDetails?.FirstOrDefault(m => (m.Kind is int k && k == 0) || (m.Type is not null && m.Type == ServiceType.App)));

        if (options.Json)
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

        Console.WriteLine($"== Koan Context: {projectName} ==");
        Console.WriteLine($"Profile: {profile} | Path: {new DirectoryInfo(cwd).Name}");
        Console.WriteLine();

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

        var appSvc = plan.Services.FirstOrDefault(s => s.Type == ServiceType.App);
        if (appSvc is not null && appSvc.Ports.Count > 0)
        {
            var appPortsText = string.Join(", ", appSvc.Ports.Select(p => $"{p.Host}:{p.Container}"));
            var src = Planner.LastPortAssignments.TryGetValue(appSvc.Id, out var alloc) ? alloc.Source : "unknown";
            var appMd = manifestDetails?.FirstOrDefault(m => m.Id.Equals(appSvc.Id, StringComparison.OrdinalIgnoreCase))
                       ?? manifestDetails?.FirstOrDefault(m => (m.Kind is int k && k == 0) || (m.Type is not null && m.Type == ServiceType.App));
            Console.WriteLine("APPLICATION   PORT          SOURCE        NETWORKS");
            Console.WriteLine($"{appSvc.Id,-13} {appPortsText,-13} {src,-13} {OrchestrationConstants.InternalNetwork} + {OrchestrationConstants.ExternalNetwork}");
            if (!string.IsNullOrWhiteSpace(appMd?.Name) && !appMd!.Name!.Equals(appSvc.Id, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{"",-13} {"NAME",-13} {appMd!.Name}");
            }
            if (!string.IsNullOrWhiteSpace(appMd?.QualifiedCode))
            {
                Console.WriteLine($"{"",-13} {"CODE",-13} {appMd!.QualifiedCode}");
            }
            var appCaps = appMd?.Capabilities is { Count: > 0 }
                ? appMd.Capabilities
                : (appMd?.Provides is { Count: > 0 } ? appMd.Provides.ToDictionary(k => k, v => (string?)string.Empty) : null);
            if (appCaps is { Count: > 0 })
            {
                var list = appCaps.Select(kv => string.IsNullOrWhiteSpace(kv.Value) ? kv.Key : $"{kv.Key}={kv.Value}").ToList();
                const int max = 6;
                var shown = list.Take(max).ToList();
                var suffix = list.Count > max ? $"  +{list.Count - max} more" : string.Empty;
                Console.WriteLine($"{"",-13} {"CAPABILITIES",-13} {string.Join(", ", shown)}{suffix}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("SERVICES      PORTS         HEALTH    TYPE        IMAGE:TAG");
        foreach (var s in plan.Services)
        {
            var md = manifestDetails?.FirstOrDefault(m => m.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
            var ports = s.Ports is null || s.Ports.Count == 0
                ? "internal"
                : s.Ports.Any(p => p.Host > 0)
                    ? string.Join(", ", s.Ports.Where(p => p.Host > 0).Select(p => p.Host.ToString()))
                    : "internal";
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
                ? (string.IsNullOrWhiteSpace(tag) ? img : img + ":" + tag)
                : string.Empty);
            Console.WriteLine($"{s.Id,-13} {ports,-13} {health,-9} {type,-11} {imageTag}");
            if (!string.IsNullOrWhiteSpace(md?.Name) && !md!.Name!.Equals(s.Id, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{"",-13} {"NAME",-13} {md!.Name}");
            }
            var svcCaps = md?.Capabilities is { Count: > 0 }
                ? md.Capabilities
                : (md?.Provides is { Count: > 0 } ? md.Provides.ToDictionary(k => k, v => (string?)string.Empty) : null);
            if (svcCaps is { Count: > 0 })
            {
                var caps = svcCaps.Select(kv => string.IsNullOrWhiteSpace(kv.Value) ? kv.Key : $"{kv.Key}={kv.Value}").ToList();
                const int maxCaps = 6;
                var shownCaps = caps.Take(maxCaps).ToList();
                var suffixCaps = caps.Count > maxCaps ? $"  +{caps.Count - maxCaps} more" : string.Empty;
                Console.WriteLine($"{"",-13} {"CAPABILITIES",-13} {string.Join(", ", shownCaps)}{suffixCaps}");
            }
        }
        Console.WriteLine();

        if (authProviders is { Count: > 0 })
        {
            Console.WriteLine($"{"AUTH",-13} {"PROVIDERS",-13} {"PROTOCOL",-9}");
            foreach (var ap in authProviders)
            {
                Console.WriteLine($"{"",-13} {ap.Name,-13} {ap.Protocol,-9}");
            }
            Console.WriteLine();
        }
        if (authCapabilities is { Count: > 0 })
        {
            Console.WriteLine($"{"AUTH CAPABILITIES",-27} {"PROTOCOL",-9}");
            foreach (var ap in authCapabilities)
            {
                Console.WriteLine($"  {ap.Name,-25} {ap.Protocol,-9}");
            }
            Console.WriteLine();
        }

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

        Console.WriteLine("COMMANDS");
        Console.WriteLine("Koan up                  # Start all services");
        Console.WriteLine("Koan status              # Check service status");
        Console.WriteLine("Koan export compose      # Generate artifacts");
        var svcHint = plan.Services.Select(s => s.Id).FirstOrDefault(id => id.Equals("api", StringComparison.OrdinalIgnoreCase))
                     ?? plan.Services.Select(s => s.Id).FirstOrDefault()
                     ?? "[service]";
        Console.WriteLine($"Koan logs --service {svcHint} --tail 100");

        return 0;
    }

    public async Task<int> ExecuteLogs(LogsCommandOptions options)
    {
        var provider = await SelectProvider(options.Engine);
        await foreach (var line in provider.Logs(new LogsOptions(Service: options.Service, Follow: options.Follow, Tail: options.Tail, Since: options.Since)))
        {
            Console.WriteLine(Orchestration.Redaction.RedactText(line));
        }
        return 0;
    }

    public Plan BuildPlan(Profile profile, int? basePort, int? portOverride, bool exposeInternals, bool persistLaunchManifest, bool autoAvoidPorts)
    {
        var plan = Planner.Build(profile);
        if (basePort.HasValue)
        {
            plan = ApplyBasePort(plan, basePort.Value);
        }
        if (profile != Profile.Prod)
        {
            plan = Planner.AssignAppPublicPort(plan, portOverride, exposeInternals, persist: persistLaunchManifest);
            if (autoAvoidPorts)
            {
                plan = PortAllocator.AutoAvoidPorts(plan);
            }
        }
        return plan;
    }

    private static Plan ApplyBasePort(Plan plan, int basePort)
    {
        IReadOnlyList<ServiceSpec> Transform()
            => plan.Services.Select(s =>
            {
                var ports = s.Ports.Select(p => (Host: basePort + p.Host, Container: p.Container)).ToList();
                return s with { Ports = ports };
            }).ToList();
        return new Plan(plan.Profile, Transform());
    }

    private async Task<IHostingProvider> SelectProvider(string? engine)
    {
        var providers = new List<IHostingProvider> { new DockerProvider(), new PodmanProvider() };
        if (!string.IsNullOrWhiteSpace(engine))
        {
            var forced = providers.FirstOrDefault(p => string.Equals(p.Id, engine, StringComparison.OrdinalIgnoreCase));
            if (forced is not null)
            {
                var (ok, _) = await forced.IsAvailable();
                if (ok) return forced;
            }
        }
        foreach (var p in OrderByPreference(providers))
        {
            var (ok, _) = await p.IsAvailable();
            if (ok) return p;
        }
        return new DockerProvider();
    }

    private IEnumerable<IHostingProvider> OrderByPreference(IEnumerable<IHostingProvider> providers)
    {
        var order = ResolveProviderOrder();
        return providers.OrderBy(p => Array.IndexOf(order, p.Id.ToLowerInvariant()));
    }

    private string[] ResolveProviderOrder()
    {
        var env = Environment.GetEnvironmentVariable(Constants.EnvPreferredProviders);
        if (string.IsNullOrWhiteSpace(env)) return Constants.DefaultProviderOrder;
        var list = env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(s => s.ToLowerInvariant())
                      .ToArray();
        return list.Length == 0 ? Constants.DefaultProviderOrder : list;
    }

    private static Dictionary<string, string>? ComputeDependenciesFromPlan(Plan plan)
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

    private static List<(string Id, string Name, string Protocol)>? DiscoverAuthProviders(string cwd)
    {
        try
        {
            var files = new[] { "appsettings.json", "appsettings.Local.json", "appsettings.Development.json" }
                .Select(f => Path.Combine(cwd, f))
                .Where(File.Exists)
                .ToArray();
            if (files.Length == 0) return null;

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
            return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static string PrettyProtocol(string? type)
        => string.IsNullOrWhiteSpace(type) ? "OIDC"
           : type!.ToLowerInvariant() switch
           {
               "oidc" => "OIDC",
               "oauth2" or "oauth" => "OAuth",
               "saml" => "SAML",
               "ldap" => "LDAP",
               _ => type
           };

    private static string Titleize(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return id;
        var parts = id.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            parts[i] = char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : string.Empty);
        }
        return string.Join(' ', parts);
    }

    public sealed record ExportCommandOptions(string Format, string OutputPath, string? Profile, int? BasePort, int? PortOverride, bool ExposeInternals, bool NoLaunchManifest);
    public sealed record DoctorCommandOptions(string? Engine, bool Json);
    public sealed record UpCommandOptions(string OutputPath, bool Explain, bool DryRun, bool Verbose, bool Trace, bool Quiet, string? Engine, string? Profile, int? TimeoutSeconds, int? BasePort, int? PortOverride, bool ExposeInternals, bool NoLaunchManifest, string? ConflictsMode);
    public sealed record DownCommandOptions(string? OutputPath, bool RemoveVolumes, string? Engine);
    public sealed record StatusCommandOptions(bool Json, string? Engine, string? Profile, int? BasePort, int? PortOverride, bool ExposeInternals, bool NoLaunchManifest);
    public sealed record InspectCommandOptions(bool Json, bool Quiet, string? Engine, string? Profile, int? BasePort, int? PortOverride, bool ExposeInternals);
    public sealed record LogsCommandOptions(bool Follow, int? Tail, string? Service, string? Since, string? Engine);
}
