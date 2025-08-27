using System.Reflection;

namespace Sora.Orchestration.Cli.Planning;

internal static class ProjectDependencyAnalyzer
{
    public static PlanDraft? DiscoverDraft(Profile profile)
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            var csproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj is null) return null;

            // Heuristic: determine if this is a web app by SDK marker
            var xml = File.ReadAllText(csproj);
            var isWeb = xml.IndexOf("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) >= 0;

            // Build a MetadataLoadContext over the app/bin + repo src outputs + core libs
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var appBin = FindAppBinDir(cwd);
            var repoRoot = Directory.GetParent(cwd)?.Parent?.FullName;
            var probePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            probePaths.Add(coreDir);
            if (appBin is { }) probePaths.Add(appBin);
            if (!string.IsNullOrEmpty(repoRoot))
            {
                var srcDir = Path.Combine(repoRoot!, "src");
                if (Directory.Exists(srcDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories)
                        .Where(p => p.Contains(Path.Combine("bin", "Debug"), StringComparison.OrdinalIgnoreCase)))
                    {
                        probePaths.Add(d);
                    }
                }
            }

            var dlls = probePaths.Where(Directory.Exists)
                .SelectMany(p => Directory.EnumerateFiles(p, "*.dll", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dlls.Count == 0)
                return new PlanDraft(Array.Empty<ServiceRequirement>(), IncludeApp: isWeb, AppHttpPort: 8080);

            var resolver = new PathAssemblyResolver(dlls);
            using var mlc = new MetadataLoadContext(resolver);
            var reqs = new List<ServiceRequirement>();

            // Prefer generated manifest if found in any loaded assembly
            foreach (var path in dlls)
            {
                try
                {
                    var asm = mlc.LoadFromAssemblyPath(path);
                    var manifestType = asm.GetType("Sora.Orchestration.__SoraOrchestrationManifest", throwOnError: false, ignoreCase: false);
                    if (manifestType is null) continue;
                    var jsonField = manifestType.GetField("Json", BindingFlags.Public | BindingFlags.Static);
                    var json = jsonField?.GetRawConstantValue() as string;
                    if (!string.IsNullOrEmpty(json))
                    {
                        var parsed = ParseManifestJson(json!);
                        if (parsed.Count > 0) reqs.AddRange(parsed);
                    }
                }
                catch { /* ignore */ }
            }

            if (reqs.Count > 0)
            {
                var distinctFromManifest = reqs
                    .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                return new PlanDraft(distinctFromManifest, IncludeApp: isWeb, AppHttpPort: 8080);
            }

            foreach (var path in dlls)
            {
                try
                {
                    var asm = mlc.LoadFromAssemblyPath(path);
                    // First, look for legacy OrchestrationServiceManifestAttribute (assembly-level)
                    foreach (var cad in asm.GetCustomAttributesData())
                    {
                        if (string.Equals(cad.AttributeType.FullName, "Sora.Orchestration.OrchestrationServiceManifestAttribute", StringComparison.Ordinal))
                        {
                            var id = cad.ConstructorArguments.Count > 0 ? cad.ConstructorArguments[0].Value as string : null;
                            var image = cad.ConstructorArguments.Count > 1 ? cad.ConstructorArguments[1].Value as string : null;
                            var ports = ExtractIntArray(cad.ConstructorArguments.Count > 2 ? cad.ConstructorArguments[2] : default);
                            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(image)) continue;

                            var env = new Dictionary<string,string?>();
                            var appEnv = new Dictionary<string,string?>();
                            var volumes = Array.Empty<string>();
                            string? healthPath = null; int? healthInterval = null; int? healthTimeout = null; int? healthRetries = null;
                            foreach (var na in cad.NamedArguments)
                            {
                                switch (na.MemberName)
                                {
                                    case nameof(OrchestrationServiceManifestAttribute.Environment):
                                        foreach (var s in ExtractStringArray(na.TypedValue)) AddKv(env, s);
                                        break;
                                    case nameof(OrchestrationServiceManifestAttribute.AppEnvironment):
                                        foreach (var s in ExtractStringArray(na.TypedValue)) AddKv(appEnv, s);
                                        break;
                                    case nameof(OrchestrationServiceManifestAttribute.Volumes):
                                        volumes = ExtractStringArray(na.TypedValue);
                                        break;
                                    case "HealthPath":
                                        healthPath = na.TypedValue.Value?.ToString();
                                        break;
                                    case "HealthIntervalSeconds":
                                        healthInterval = (int?)na.TypedValue.Value;
                                        break;
                                    case "HealthTimeoutSeconds":
                                        healthTimeout = (int?)na.TypedValue.Value;
                                        break;
                                    case "HealthRetries":
                                        healthRetries = (int?)na.TypedValue.Value;
                                        break;
                                }
                            }
                            reqs.Add(new ServiceRequirement(id!, image!, env, ports, volumes, appEnv, null, null, null, null, null, null, null, healthPath, healthInterval, healthTimeout, healthRetries));
                        }
                    }

                    // Then, try class-level Default* attributes (new model)
                    foreach (var type in asm.GetTypes())
                    {
                        // We only care about public, non-abstract classes to keep scan small
                        if (!type.IsClass || type.IsAbstract) continue;
                        string? sid = null; string? image = null; int[] ports = Array.Empty<int>();
                        var env = new Dictionary<string,string?>();
                        var appEnv = new Dictionary<string,string?>();
                        var volumes = new List<string>();
                        string? endpointScheme = null; string? endpointHost = null; string? endpointPattern = null;
                        string? localScheme = null; string? localHost = null; int? localPort = null; string? localPattern = null;
                        string? healthPath = null; int? healthInterval = null; int? healthTimeout = null; int? healthRetries = null;

                        foreach (var cad in type.GetCustomAttributesData())
                        {
                            var full = cad.AttributeType.FullName;
                            if (string.Equals(full, "Sora.Orchestration.Abstractions.Attributes.ServiceIdAttribute", StringComparison.Ordinal))
                            {
                                sid = cad.ConstructorArguments.Count > 0 ? cad.ConstructorArguments[0].Value as string : sid;
                            }
                            else if (string.Equals(full, "Sora.Orchestration.Abstractions.Attributes.ContainerDefaultsAttribute", StringComparison.Ordinal))
                            {
                                image = cad.ConstructorArguments.Count > 0 ? cad.ConstructorArguments[0].Value as string : image;
                                foreach (var na in cad.NamedArguments)
                                {
                                    switch (na.MemberName)
                                    {
                                        case "Tag":
                                            var tag = na.TypedValue.Value?.ToString();
                                            if (!string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(image)) image = image + ":" + tag;
                                            break;
                                        case "Ports":
                                            ports = ExtractIntArray(na.TypedValue);
                                            break;
                                        case "Env":
                                            foreach (var s in ExtractStringArray(na.TypedValue)) AddKv(env, s);
                                            break;
                                        case "Volumes":
                                            foreach (var s in ExtractStringArray(na.TypedValue)) volumes.Add(s);
                                            break;
                                    }
                                }
                            }
                            else if (string.Equals(full, "Sora.Orchestration.Abstractions.Attributes.EndpointDefaultsAttribute", StringComparison.Ordinal))
                            {
                                // Constructor: (EndpointMode mode, string scheme, string host, int port)
                                if (cad.ConstructorArguments.Count >= 4)
                                {
                                    var modeArg = cad.ConstructorArguments[0];
                                    bool isContainer = false;
                                    try
                                    {
                                        if (modeArg.Value is int i)
                                        {
                                            // EndpointMode.Container == 0
                                            isContainer = i == 0;
                                        }
                                        else
                                        {
                                            var s = modeArg.Value?.ToString() ?? string.Empty;
                                            isContainer = s.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0;
                                        }
                                    }
                                    catch { }

                                    // We prefer Container mode endpoint for compose
                                    if (isContainer)
                                    {
                                        endpointScheme = cad.ConstructorArguments[1].Value?.ToString();
                                        endpointHost = cad.ConstructorArguments[2].Value?.ToString();
                                        foreach (var na in cad.NamedArguments)
                                        {
                                            if (na.MemberName == "UriPattern") endpointPattern = na.TypedValue.Value?.ToString();
                                        }
                                    }
                                    else
                                    {
                                        localScheme = cad.ConstructorArguments[1].Value?.ToString();
                                        localHost = cad.ConstructorArguments[2].Value?.ToString();
                                        localPort = cad.ConstructorArguments.Count >= 4 ? (int?)cad.ConstructorArguments[3].Value : null;
                                        foreach (var na in cad.NamedArguments)
                                        {
                                            if (na.MemberName == "UriPattern") localPattern = na.TypedValue.Value?.ToString();
                                        }
                                    }
                                }
                            }
                            else if (string.Equals(full, "Sora.Orchestration.Abstractions.Attributes.AppEnvDefaultsAttribute", StringComparison.Ordinal))
                            {
                                if (cad.ConstructorArguments.Count > 0)
                                {
                                    foreach (var kv in ExtractStringArray(cad.ConstructorArguments[0])) AddKv(appEnv, kv);
                                }
                            }
                            else if (string.Equals(full, "Sora.Orchestration.Abstractions.Attributes.HealthEndpointDefaultsAttribute", StringComparison.Ordinal))
                            {
                                if (cad.ConstructorArguments.Count > 0)
                                {
                                    healthPath = cad.ConstructorArguments[0].Value?.ToString();
                                }
                                foreach (var na in cad.NamedArguments)
                                {
                                    switch (na.MemberName)
                                    {
                                        case "IntervalSeconds": healthInterval = (int?)na.TypedValue.Value; break;
                                        case "TimeoutSeconds": healthTimeout = (int?)na.TypedValue.Value; break;
                                        case "Retries": healthRetries = (int?)na.TypedValue.Value; break;
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(sid) && !string.IsNullOrWhiteSpace(image))
                        {
                            reqs.Add(new ServiceRequirement(sid!, image!, env, ports, volumes.ToArray(), appEnv,
                                endpointScheme, endpointHost, endpointPattern, localScheme, localHost, localPort, localPattern,
                                healthPath, healthInterval, healthTimeout, healthRetries));
                        }
                    }
                }
                catch
                {
                    // ignore broken assemblies
                }
            }

            // De-dupe by id (prefer first occurrence in app/repo probing order)
            var distinct = reqs
                .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            return new PlanDraft(distinct, IncludeApp: isWeb, AppHttpPort: 8080);
        }
        catch
        {
            return null;
        }
    }

    private static List<ServiceRequirement> ParseManifestJson(string json)
    {
        var list = new List<ServiceRequirement>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("services", out var services) || services.ValueKind != System.Text.Json.JsonValueKind.Array)
                return list;
            foreach (var el in services.EnumerateArray())
            {
                string id = el.GetProperty("id").GetString() ?? string.Empty;
                string image = el.GetProperty("image").GetString() ?? string.Empty;
                var ports = el.TryGetProperty("ports", out var portsEl) && portsEl.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? portsEl.EnumerateArray().Select(p => p.GetInt32()).ToArray()
                    : Array.Empty<int>();
                var env = new Dictionary<string,string?>();
                if (el.TryGetProperty("env", out var envEl) && envEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var kv in envEl.EnumerateObject()) env[kv.Name] = kv.Value.GetString();
                }
                var appEnv = new Dictionary<string,string?>();
                if (el.TryGetProperty("appEnv", out var appEnvEl) && appEnvEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var kv in appEnvEl.EnumerateObject()) appEnv[kv.Name] = kv.Value.GetString();
                }
                var volumes = Array.Empty<string>();
                if (el.TryGetProperty("volumes", out var volsEl) && volsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    volumes = volsEl.EnumerateArray().Select(v => v.GetString() ?? string.Empty).Where(v => v.Length > 0).ToArray();
                }
                string? scheme = el.TryGetProperty("scheme", out var sc) ? sc.GetString() : null;
                string? host = el.TryGetProperty("host", out var ho) ? ho.GetString() : null;
                string? uriPattern = el.TryGetProperty("uriPattern", out var up) ? up.GetString() : null;
                string? localScheme = el.TryGetProperty("localScheme", out var ls) ? ls.GetString() : null;
                string? localHost = el.TryGetProperty("localHost", out var lh) ? lh.GetString() : null;
                int? localPort = el.TryGetProperty("localPort", out var lp) && lp.ValueKind == System.Text.Json.JsonValueKind.Number ? lp.GetInt32() : null;
                string? localPattern = el.TryGetProperty("localPattern", out var lpn) ? lpn.GetString() : null;
                string? healthPath = el.TryGetProperty("healthPath", out var hp) ? hp.GetString() : null;
                int? healthInterval = el.TryGetProperty("healthInterval", out var hi) && hi.ValueKind == System.Text.Json.JsonValueKind.Number ? hi.GetInt32() : null;
                int? healthTimeout = el.TryGetProperty("healthTimeout", out var ht) && ht.ValueKind == System.Text.Json.JsonValueKind.Number ? ht.GetInt32() : null;
                int? healthRetries = el.TryGetProperty("healthRetries", out var hr) && hr.ValueKind == System.Text.Json.JsonValueKind.Number ? hr.GetInt32() : null;
                // endpointPort is optional and not part of ServiceRequirement; scheme/host used for token replacement
                list.Add(new ServiceRequirement(id, image, env, ports, volumes, appEnv, scheme, host, uriPattern, localScheme, localHost, localPort, localPattern,
                    healthPath, healthInterval, healthTimeout, healthRetries));
            }
        }
        catch { }
        return list;
    }

    private static string? FindAppBinDir(string projectDir)
    {
        var bin = Path.Combine(projectDir, "bin");
        if (!Directory.Exists(bin)) return null;
        // Prefer Debug
        var cfg = Directory.Exists(Path.Combine(bin, "Debug")) ? Path.Combine(bin, "Debug") : bin;
        // Find most recent TFM folder
        var tfm = Directory.EnumerateDirectories(cfg, "net*", SearchOption.AllDirectories)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return tfm ?? cfg;
    }

    private static int[] ExtractIntArray(CustomAttributeTypedArgument arg)
    {
        try
        {
            if (arg.ArgumentType is null) return Array.Empty<int>();
            if (!arg.ArgumentType.IsArray) return Array.Empty<int>();
            var list = arg.Value as IReadOnlyCollection<CustomAttributeTypedArgument>;
            if (list is null) return Array.Empty<int>();
            return list.Select(x => (int)(x.Value ?? 0)).ToArray();
        }
        catch { return Array.Empty<int>(); }
    }

    private static string[] ExtractStringArray(CustomAttributeTypedArgument arg)
    {
        try
        {
            if (arg.ArgumentType is null) return Array.Empty<string>();
            if (!arg.ArgumentType.IsArray) return Array.Empty<string>();
            var list = arg.Value as IReadOnlyCollection<CustomAttributeTypedArgument>;
            if (list is null) return Array.Empty<string>();
            return list.Select(x => x.Value?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static void AddKv(Dictionary<string,string?> target, string kv)
    {
        var idx = kv.IndexOf('=');
        if (idx > 0) target[kv[..idx]] = kv[(idx+1)..];
    }
}
