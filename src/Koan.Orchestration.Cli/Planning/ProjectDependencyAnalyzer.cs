using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using Koan.Orchestration.Attributes;
using Koan.Orchestration.Models;
using Koan.Orchestration.Planning;

namespace Koan.Orchestration.Cli.Planning;

internal static class ProjectDependencyAnalyzer
{
    public static List<(string Id, string Name, string Protocol, string? Icon)>? ManifestAuthProviders { get; private set; }
    public static List<string>? ManifestIdDuplicates { get; private set; }

    // Lightweight manifest service view used by Inspect
    public sealed class ManifestServiceDetails
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int? Kind { get; set; } // ARCH-0049 ServiceKind
        public ServiceType? Type { get; set; } // legacy mapping if present
        public string? QualifiedCode { get; set; }
        public string? ContainerImage { get; set; }
        public string? DefaultTag { get; set; }
        public int[]? DefaultPorts { get; set; }
        public string? HealthEndpoint { get; set; }
        public List<string>? Provides { get; set; }
        public List<string>? Consumes { get; set; }
        public Dictionary<string, string?>? Capabilities { get; set; }
    }

    /// <summary>
    /// Discover services declared via [KoanService]/manifest (read-only) by reading generated __KoanOrchestrationManifest.Json.
    /// Returns a distinct list of (Id, Name, Type) across all assemblies in the app bin.
    /// </summary>
    public static List<(string Id, string? Name, ServiceType? Type)>? DiscoverServicesFromManifest()
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            var csproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj is null) return null;

            // Probe paths (same strategy as DiscoverDraft)
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var appBin = FindAppBinDir(cwd);
            string? repoRoot = null;
            var p = new DirectoryInfo(cwd);
            for (int i = 0; i < 5 && p is not null; i++, p = p.Parent)
            {
                if (p.GetDirectories("src", SearchOption.TopDirectoryOnly).Any()) { repoRoot = p.FullName; break; }
            }

            var resolverProbePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { coreDir };
            if (!string.IsNullOrEmpty(appBin)) resolverProbePaths.Add(appBin!);
            if (!string.IsNullOrEmpty(repoRoot))
            {
                var srcDir = Path.Combine(repoRoot!, "src");
                if (Directory.Exists(srcDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories)
                        .Where(pp => pp.Contains(Path.Combine("bin", "Debug"), StringComparison.OrdinalIgnoreCase)
                                  || pp.Contains(Path.Combine("bin", "Release"), StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var tfm in Directory.EnumerateDirectories(d, "net*", SearchOption.TopDirectoryOnly))
                            resolverProbePaths.Add(tfm);
                    }
                }
            }

            // Locate the app assembly
            var appAssemblyName = Path.GetFileNameWithoutExtension(csproj);
            string? appAsmPath = null;
            if (!string.IsNullOrEmpty(appBin))
            {
                var candidate = Path.Combine(appBin!, appAssemblyName + ".dll");
                if (File.Exists(candidate)) appAsmPath = candidate;
                else
                {
                    var dirName = new DirectoryInfo(cwd).Name;
                    candidate = Path.Combine(appBin!, dirName + ".dll");
                    if (File.Exists(candidate)) appAsmPath = candidate;
                    else appAsmPath = Directory.EnumerateFiles(appBin!, "*.dll", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));
                }
            }
            if (string.IsNullOrEmpty(appAsmPath)) return null;

            var allDlls = resolverProbePaths.Where(Directory.Exists)
                .SelectMany(p2 => Directory.EnumerateFiles(p2, "*.dll", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolver = new PathAssemblyResolver(allDlls);
            using var mlc = new MetadataLoadContext(resolver);

            // Scan set: the app's bin folder (captures optional adapters) + ensure app assembly
            var scanDlls = new List<string>();
            try { scanDlls.AddRange(Directory.EnumerateFiles(appBin!, "*.dll", SearchOption.TopDirectoryOnly)); } catch { }
            if (!scanDlls.Contains(appAsmPath!, StringComparer.OrdinalIgnoreCase)) scanDlls.Add(appAsmPath!);

            var list = new List<(string Id, string? Name, ServiceType? Type)>();
            foreach (var path in scanDlls)
            {
                try
                {
                    var asm = mlc.LoadFromAssemblyPath(path);
                    var t = asm.GetType("Koan.Orchestration.__KoanOrchestrationManifest", throwOnError: false, ignoreCase: false);
                    if (t is null) continue;
                    var json = t.GetField("Json", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var root = JToken.Parse(json!);
                    var services = root?["services"] as JArray;
                    if (services is null) continue;
                    foreach (var el in services.OfType<JObject>())
                    {
                        var id = (string?)(el["shortCode"] ?? el["id"]);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        string? name = (string?)el["name"];
                        ServiceType? type = null;
                        var tVal = el["type"];
                        if (tVal != null && tVal.Type == JTokenType.Integer)
                        {
                            try { type = (ServiceType)(int)tVal; } catch { }
                        }
                        if (type is null)
                        {
                            var kVal = el["kind"];
                            if (kVal != null && kVal.Type == JTokenType.Integer)
                            {
                                try { var k = (int)kVal; type = k switch { 0 => ServiceType.App, 1 => ServiceType.Database, 2 => ServiceType.Vector, 3 => ServiceType.Ai, _ => ServiceType.Service }; } catch { }
                            }
                        }
                        list.Add((id!, name, type));
                    }
                }
                catch { }
            }

            if (list.Count == 0) { ManifestIdDuplicates = null; return null; }

            // Compute duplicates across all manifests (case-insensitive)
            var dups = list
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ManifestIdDuplicates = dups.Count == 0 ? null : dups;

            return list
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return null; }
    }

    /// <summary>
    /// Discover full manifest-declared service details for the current project (read-only).
    /// </summary>
    public static List<ManifestServiceDetails>? DiscoverManifestServiceDetails()
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            var csproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj is null) return null;

            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var appBin = FindAppBinDir(cwd);
            string? repoRoot = null;
            var p = new DirectoryInfo(cwd);
            for (int i = 0; i < 5 && p is not null; i++, p = p.Parent)
            {
                if (p.GetDirectories("src", SearchOption.TopDirectoryOnly).Any()) { repoRoot = p.FullName; break; }
            }

            var resolverProbePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { coreDir };
            if (!string.IsNullOrEmpty(appBin)) resolverProbePaths.Add(appBin!);
            if (!string.IsNullOrEmpty(repoRoot))
            {
                var srcDir = Path.Combine(repoRoot!, "src");
                if (Directory.Exists(srcDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories)
                        .Where(pp => pp.Contains(Path.Combine("bin", "Debug"), StringComparison.OrdinalIgnoreCase)
                                  || pp.Contains(Path.Combine("bin", "Release"), StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var tfm in Directory.EnumerateDirectories(d, "net*", SearchOption.TopDirectoryOnly))
                            resolverProbePaths.Add(tfm);
                    }
                }
            }

            // Locate the app assembly
            var appAssemblyName = Path.GetFileNameWithoutExtension(csproj);
            string? appAsmPath = null;
            if (!string.IsNullOrEmpty(appBin))
            {
                var candidate = Path.Combine(appBin!, appAssemblyName + ".dll");
                if (File.Exists(candidate)) appAsmPath = candidate;
                else
                {
                    var dirName = new DirectoryInfo(cwd).Name;
                    candidate = Path.Combine(appBin!, dirName + ".dll");
                    if (File.Exists(candidate)) appAsmPath = candidate;
                    else appAsmPath = Directory.EnumerateFiles(appBin!, "*.dll", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));
                }
            }
            if (string.IsNullOrEmpty(appAsmPath)) return null;

            var allDlls = resolverProbePaths.Where(Directory.Exists)
                .SelectMany(p2 => Directory.EnumerateFiles(p2, "*.dll", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolver = new PathAssemblyResolver(allDlls);
            using var mlc = new MetadataLoadContext(resolver);

            // Scan set: the app's bin folder + ensure app assembly
            var scanDlls = new List<string>();
            try { scanDlls.AddRange(Directory.EnumerateFiles(appBin!, "*.dll", SearchOption.TopDirectoryOnly)); } catch { }
            if (!scanDlls.Contains(appAsmPath!, StringComparer.OrdinalIgnoreCase)) scanDlls.Add(appAsmPath!);

            var list = new List<ManifestServiceDetails>();
            foreach (var path in scanDlls)
            {
                try
                {
                    var asm = mlc.LoadFromAssemblyPath(path);
                    var t = asm.GetType("Koan.Orchestration.__KoanOrchestrationManifest", throwOnError: false, ignoreCase: false);
                    if (t is null) continue;
                    var json = t.GetField("Json", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var root = JToken.Parse(json!);
                    var services = root?["services"] as JArray;
                    if (services is null) continue;
                    foreach (var el in services.OfType<JObject>())
                    {
                        var id = (string?)(el["shortCode"] ?? el["id"]);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var det = new ManifestServiceDetails { Id = id! };
                        det.Name = (string?)el["name"];
                        det.Kind = (int?)el["kind"];
                        var tVal = el["type"];
                        if (tVal != null && tVal.Type == JTokenType.Integer)
                        {
                            try { det.Type = (ServiceType)(int)tVal; } catch { }
                        }
                        det.QualifiedCode = (string?)el["qualifiedCode"];
                        det.ContainerImage = (string?)el["containerImage"];
                        det.DefaultTag = (string?)el["defaultTag"];
                        var dp = el["defaultPorts"] as JArray;
                        det.DefaultPorts = dp is null ? null : dp.Select(x => (int)x).ToArray();
                        det.HealthEndpoint = (string?)el["healthEndpoint"];
                        var pr = el["provides"] as JArray; det.Provides = pr?.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
                        var co = el["consumes"] as JArray; det.Consumes = co?.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
                        var cap = el["capabilities"] as JObject;
                        if (cap is not null)
                        {
                            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                            foreach (var prop in cap.Properties()) map[prop.Name] = prop.Value.Type == JTokenType.String ? (string?)prop.Value : prop.Value.ToString();
                            det.Capabilities = map;
                        }
                        list.Add(det);
                    }
                }
                catch { }
            }

            if (list.Count == 0) return null;
            return list
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return null; }
    }

    /// <summary>
    /// Discover Koan.* assembly references for the current project (best-effort, read-only).
    /// Returns simple assembly names (e.g., Koan.Data.Connector.Postgres, Koan.Web, Koan.Messaging.Connector.RabbitMq).
    /// </summary>
    public static List<string>? DiscoverKoanReferences()
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            var csproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj is null) return null;

            // Probe paths (same strategy as DiscoverDraft)
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var appBin = FindAppBinDir(cwd);
            string? repoRoot = null;
            var p = new DirectoryInfo(cwd);
            for (int i = 0; i < 5 && p is not null; i++, p = p.Parent)
            {
                if (p.GetDirectories("src", SearchOption.TopDirectoryOnly).Any()) { repoRoot = p.FullName; break; }
            }

            var resolverProbePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { coreDir };
            if (!string.IsNullOrEmpty(appBin)) resolverProbePaths.Add(appBin!);
            if (!string.IsNullOrEmpty(repoRoot))
            {
                var srcDir = Path.Combine(repoRoot!, "src");
                if (Directory.Exists(srcDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories)
                        .Where(pp => pp.Contains(Path.Combine("bin", "Debug"), StringComparison.OrdinalIgnoreCase)
                                  || pp.Contains(Path.Combine("bin", "Release"), StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var tfm in Directory.EnumerateDirectories(d, "net*", SearchOption.TopDirectoryOnly))
                            resolverProbePaths.Add(tfm);
                    }
                }
            }

            // Locate the app assembly
            var appAssemblyName = Path.GetFileNameWithoutExtension(csproj);
            string? appAsmPath = null;
            if (!string.IsNullOrEmpty(appBin))
            {
                var candidate = Path.Combine(appBin!, appAssemblyName + ".dll");
                if (File.Exists(candidate)) appAsmPath = candidate;
                else
                {
                    var dirName = new DirectoryInfo(cwd).Name;
                    candidate = Path.Combine(appBin!, dirName + ".dll");
                    if (File.Exists(candidate)) appAsmPath = candidate;
                    else appAsmPath = Directory.EnumerateFiles(appBin!, "*.dll", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));
                }
            }
            if (string.IsNullOrEmpty(appAsmPath)) return null;

            var allDlls = resolverProbePaths.Where(Directory.Exists)
                .SelectMany(p2 => Directory.EnumerateFiles(p2, "*.dll", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolver = new PathAssemblyResolver(allDlls);
            using var mlc = new MetadataLoadContext(resolver);

            var byName = allDlls.GroupBy(f => Path.GetFileNameWithoutExtension(f)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var q = new Queue<string>();
            q.Enqueue(appAsmPath!);
            while (q.Count > 0)
            {
                var next = q.Dequeue();
                if (!closure.Add(next)) continue;
                try
                {
                    var asm = mlc.LoadFromAssemblyPath(next);
                    foreach (var rn in asm.GetReferencedAssemblies())
                        if (byName.TryGetValue(rn.Name!, out var dep)) q.Enqueue(dep);
                }
                catch { }
            }

            // Map to simple names and pick Koan.*
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in closure)
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.StartsWith("Koan.", StringComparison.OrdinalIgnoreCase))
                        names.Add(name);
                }
                catch { }
            }
            if (names.Count == 0) return null;
            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return null; }
    }

    public static PlanDraft? DiscoverDraft(Profile profile)
    {
        try
        {
            var authAccum = new List<(string Id, string Name, string Protocol, string? Icon)>();
            var includeApp = false;
            int? appHttpPort = null;
            var cwd = Directory.GetCurrentDirectory();
            var csproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj is null) return null;

            // Web SDK check removed from discovery; app inclusion is declared in manifest (Kind==App).

            // Probe paths to resolve metadata dependencies
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var appBin = FindAppBinDir(cwd);

            // Find repo root (contains 'src') to collect additional TFM folders only for resolution
            string? repoRoot = null;
            var p = new DirectoryInfo(cwd);
            for (int i = 0; i < 5 && p is not null; i++, p = p.Parent)
            {
                if (p.GetDirectories("src", SearchOption.TopDirectoryOnly).Any()) { repoRoot = p.FullName; break; }
            }

            var resolverProbePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { coreDir };
            if (!string.IsNullOrEmpty(appBin)) resolverProbePaths.Add(appBin!);
            if (!string.IsNullOrEmpty(repoRoot))
            {
                var srcDir = Path.Combine(repoRoot!, "src");
                if (Directory.Exists(srcDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories)
                        .Where(pp => pp.Contains(Path.Combine("bin", "Debug"), StringComparison.OrdinalIgnoreCase)
                                  || pp.Contains(Path.Combine("bin", "Release"), StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var tfm in Directory.EnumerateDirectories(d, "net*", SearchOption.TopDirectoryOnly))
                            resolverProbePaths.Add(tfm);
                    }
                }
            }

            // Locate the app assembly
            var appAssemblyName = Path.GetFileNameWithoutExtension(csproj);
            string? appAsmPath = null;
            if (!string.IsNullOrEmpty(appBin))
            {
                var candidate = Path.Combine(appBin!, appAssemblyName + ".dll");
                if (File.Exists(candidate)) appAsmPath = candidate;
                else
                {
                    var dirName = new DirectoryInfo(cwd).Name;
                    candidate = Path.Combine(appBin!, dirName + ".dll");
                    if (File.Exists(candidate)) appAsmPath = candidate;
                    else appAsmPath = Directory.EnumerateFiles(appBin!, "*.dll", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));
                }
            }

            if (string.IsNullOrEmpty(appAsmPath))
                return new PlanDraft(Array.Empty<ServiceRequirement>(), IncludeApp: false, AppHttpPort: 0);

            var allDlls = resolverProbePaths.Where(Directory.Exists)
                .SelectMany(p2 => Directory.EnumerateFiles(p2, "*.dll", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolver = new PathAssemblyResolver(allDlls);
            using var mlc = new MetadataLoadContext(resolver);

            // Dependency closure (not currently used to filter, but kept if needed later)
            var byName = allDlls.GroupBy(f => Path.GetFileNameWithoutExtension(f)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var q = new Queue<string>();
            q.Enqueue(appAsmPath!);
            while (q.Count > 0)
            {
                var next = q.Dequeue();
                if (!closure.Add(next)) continue;
                try
                {
                    var asm = mlc.LoadFromAssemblyPath(next);
                    foreach (var rn in asm.GetReferencedAssemblies())
                        if (byName.TryGetValue(rn.Name!, out var dep)) q.Enqueue(dep);
                }
                catch { }
            }

            // Scan set: the app's bin folder (captures optional adapters) + ensure app assembly
            var scanDlls = new List<string>();
            try { scanDlls.AddRange(Directory.EnumerateFiles(appBin!, "*.dll", SearchOption.TopDirectoryOnly)); } catch { }
            if (!scanDlls.Contains(appAsmPath!, StringComparer.OrdinalIgnoreCase)) scanDlls.Add(appAsmPath!);

            var reqs = new List<ServiceRequirement>();

            // 1) Generated manifest JSON (preferred)
            foreach (var path in scanDlls)
            {
                try
                {
                    var asm = mlc.LoadFromAssemblyPath(path);
                    var t = asm.GetType("Koan.Orchestration.__KoanOrchestrationManifest", throwOnError: false, ignoreCase: false);
                    if (t is null) continue;
                    var json = t.GetField("Json", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;
                    if (!string.IsNullOrEmpty(json))
                    {
                        reqs.AddRange(ParseManifestJson(json!));
                        var aps = ParseAuthProviders(json!);
                        if (aps is { Count: > 0 }) authAccum.AddRange(aps);
                        // Capture app block if present (ARCH-0049)
                        try
                        {
                            var root = JToken.Parse(json!);
                            var appObj = root?["app"] as JObject;
                            if (appObj is not null)
                            {
                                includeApp = true;
                                appHttpPort = (int?)appObj["defaultPublicPort"];
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Legacy attribute-based fallback removed (ARCH-0049): manifests are the single source of truth.

            // De-dup by id, keep first occurrence (manifest wins over attributes by ordering above)
            var distinct = reqs
                .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // Union auth providers across manifests by id
            if (authAccum.Count > 0)
            {
                ManifestAuthProviders = authAccum
                    .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Default app port if included but not specified
            var finalAppPort = includeApp ? (appHttpPort ?? 8080) : 0;
            return new PlanDraft(distinct, IncludeApp: includeApp, AppHttpPort: finalAppPort);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindAppBinDir(string projectDir)
    {
        try
        {
            var bin = Path.Combine(projectDir, "bin");
            if (!Directory.Exists(bin)) return null;
            var cands = new List<DirectoryInfo>();
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var cfgDir = Path.Combine(bin, cfg);
                if (!Directory.Exists(cfgDir)) continue;
                foreach (var tfm in Directory.EnumerateDirectories(cfgDir, "net*", SearchOption.TopDirectoryOnly))
                    cands.Add(new DirectoryInfo(tfm));
            }
            if (cands.Count == 0) return null;
            return cands.OrderByDescending(d => d.LastWriteTimeUtc).First().FullName;
        }
        catch { return null; }
    }

    private static List<ServiceRequirement> ParseManifestJson(string json)
    {
        var list = new List<ServiceRequirement>();
        try
        {
            var root = JToken.Parse(json);
            var services = root?["services"] as JArray;
            if (services is null)
                return list;
            foreach (var el in services.OfType<JObject>())
            {
                var id = (string?)(el["shortCode"] ?? el["id"]);
                string? image = null;
                var repo = (string?)el["containerImage"];
                var tag = (string?)el["defaultTag"];
                if (!string.IsNullOrWhiteSpace(repo)) image = string.IsNullOrWhiteSpace(tag) ? repo : (repo + ":" + tag);
                image ??= (string?)el["image"];
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(image)) continue;

                int[] ports = (el["defaultPorts"] as JArray)?.Select(x => (int)x).ToArray()
                    ?? (el["ports"] as JArray)?.Select(x => (int)x).ToArray() ?? Array.Empty<int>();
                var env = el["env"] is JObject envObj
                    ? envObj.Properties().ToDictionary(p => p.Name, p => (string?)p.Value, StringComparer.Ordinal)
                    : new Dictionary<string, string?>();
                string[] volumes = (el["volumes"] as JArray)?.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToArray() ?? Array.Empty<string>();
                var appEnv = el["appEnv"] is JObject appEnvObj
                    ? appEnvObj.Properties().ToDictionary(p => p.Name, p => (string?)p.Value, StringComparer.Ordinal)
                    : new Dictionary<string, string?>();

                string? endpointScheme = (string?)el["scheme"];
                string? endpointHost = (string?)el["host"];
                string? endpointPattern = (string?)el["uriPattern"];
                string? localScheme = (string?)el["localScheme"];
                string? localHost = (string?)el["localHost"];
                int? localPort = (int?)el["localPort"];
                string? localPattern = (string?)el["localPattern"];
                string? healthPath = (string?)(el["healthEndpoint"] ?? el["healthPath"]);
                int? healthInterval = (int?)el["healthInterval"];
                int? healthTimeout = (int?)el["healthTimeout"];
                int? healthRetries = (int?)el["healthRetries"];

                ServiceType? type = null;
                var tVal = el["type"];
                if (tVal != null && tVal.Type == JTokenType.Integer) { try { type = (ServiceType)(int)tVal; } catch { } }
                if (type is null)
                {
                    var kVal = el["kind"];
                    if (kVal != null && kVal.Type == JTokenType.Integer) { try { var k = (int)kVal; type = k switch { 0 => ServiceType.App, 1 => ServiceType.Database, 2 => ServiceType.Vector, 3 => ServiceType.Ai, _ => ServiceType.Service }; } catch { } }
                }
                string? name = (string?)el["name"];
                string? qCode = (string?)el["qualifiedCode"];
                string? subtype = (string?)el["subtype"];
                int? deployment = (int?)(el["deploymentKind"] ?? el["deployment"]);
                string? desc = (string?)el["description"];
                List<string>? provides = (el["provides"] as JArray)?.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
                List<string>? consumes = (el["consumes"] as JArray)?.Select(x => (string?)x).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();

                list.Add(new ServiceRequirement(id!, image!, env, ports, volumes, appEnv,
                    type,
                    endpointScheme, endpointHost, endpointPattern, localScheme, localHost, localPort, localPattern,
                    healthPath, healthInterval, healthTimeout, healthRetries,
                    name, qCode, subtype, deployment, desc, provides, consumes));
            }
        }
        catch { }
        return list;
    }

    private static List<(string Id, string Name, string Protocol, string? Icon)>? ParseAuthProviders(string json)
    {
        try
        {
            var root = JToken.Parse(json);
            var arr = root?["authProviders"] as JArray;
            if (arr is null)
                return null;
            var list = new List<(string, string, string, string?)>();
            foreach (var el in arr.OfType<JObject>())
            {
                var id = (string?)el["id"];
                var name = (string?)el["name"];
                var protocol = (string?)el["protocol"] ?? string.Empty;
                var icon = (string?)el["icon"];
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    list.Add((id!, name!, protocol, icon));
            }
            return list.Count == 0 ? null : list;
        }
        catch { return null; }
    }

    private static int[] ExtractIntArray(CustomAttributeTypedArgument arg)
    {
        try
        {
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
            if (arg.ArgumentType is null || !arg.ArgumentType.IsArray) return Array.Empty<string>();
            var list = arg.Value as IReadOnlyCollection<CustomAttributeTypedArgument>;
            if (list is null) return Array.Empty<string>();
            return list.Select(x => x.Value?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static void AddKv(Dictionary<string, string?> target, string kv)
    {
        var idx = kv.IndexOf('=');
        if (idx > 0) target[kv[..idx]] = kv[(idx + 1)..];
    }

    private static bool TryAddFromAssemblyAttributes(Assembly asm, List<ServiceRequirement> reqs)
    {
        var added = false;
        try
        {
            foreach (var cad in asm.GetCustomAttributesData())
            {
                if (!string.Equals(cad.AttributeType.FullName, "Koan.Orchestration.OrchestrationServiceManifestAttribute", StringComparison.Ordinal))
                    continue;
                var id = cad.ConstructorArguments.Count > 0 ? cad.ConstructorArguments[0].Value as string : null;
                var image = cad.ConstructorArguments.Count > 1 ? cad.ConstructorArguments[1].Value as string : null;
                var ports = ExtractIntArray(cad.ConstructorArguments.Count > 2 ? cad.ConstructorArguments[2] : default);
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(image)) continue;

                var env = new Dictionary<string, string?>();
                var appEnv = new Dictionary<string, string?>();
                var volumes = Array.Empty<string>();
                ServiceType? type = null;
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
                        case nameof(OrchestrationServiceManifestAttribute.Type):
                            if (na.TypedValue.Value is int et) type = (ServiceType)et;
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
                reqs.Add(new ServiceRequirement(id!, image!, env, ports, volumes, appEnv, type, null, null, null, null, null, null, null, healthPath, healthInterval, healthTimeout, healthRetries));
                added = true;
            }
        }
        catch { }
        return added;
    }

    private static bool TryAddFromTypeAttributes(Assembly asm, List<ServiceRequirement> reqs)
    {
        var added = false;
        try
        {
            foreach (var type in asm.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;
                string? sid = null; string? image = null; int[] ports = Array.Empty<int>();
                var env = new Dictionary<string, string?>();
                var appEnv = new Dictionary<string, string?>();
                var volumes = new List<string>();
                string? endpointScheme = null; string? endpointHost = null; string? endpointPattern = null;
                string? localScheme = null; string? localHost = null; int? localPort = null; string? localPattern = null;
                string? healthPath = null; int? healthInterval = null; int? healthTimeout = null; int? healthRetries = null;
                ServiceType? svcType = null;

                foreach (var cad in type.GetCustomAttributesData())
                {
                    var full = cad.AttributeType.FullName;
                    if (string.Equals(full, "Koan.Orchestration.Abstractions.Attributes.ServiceIdAttribute", StringComparison.Ordinal))
                    {
                        sid = cad.ConstructorArguments.Count > 0 ? cad.ConstructorArguments[0].Value as string : sid;
                    }
                    else if (string.Equals(full, "Koan.Orchestration.Abstractions.Attributes.ContainerDefaultsAttribute", StringComparison.Ordinal))
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
                    else if (string.Equals(full, "Koan.Orchestration.Abstractions.Attributes.EndpointDefaultsAttribute", StringComparison.Ordinal))
                    {
                        if (cad.ConstructorArguments.Count >= 4)
                        {
                            var modeArg = cad.ConstructorArguments[0];
                            bool isContainer = false;
                            try
                            {
                                if (modeArg.Value is int i) isContainer = i == 0; else isContainer = (modeArg.Value?.ToString() ?? string.Empty).IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0;
                            }
                            catch { }
                            if (isContainer)
                            {
                                endpointScheme = cad.ConstructorArguments[1].Value?.ToString();
                                endpointHost = cad.ConstructorArguments[2].Value?.ToString();
                                foreach (var na in cad.NamedArguments)
                                    if (na.MemberName == "UriPattern") endpointPattern = na.TypedValue.Value?.ToString();
                            }
                            else
                            {
                                localScheme = cad.ConstructorArguments[1].Value?.ToString();
                                localHost = cad.ConstructorArguments[2].Value?.ToString();
                                localPort = cad.ConstructorArguments.Count >= 4 ? (int?)cad.ConstructorArguments[3].Value : null;
                                foreach (var na in cad.NamedArguments)
                                    if (na.MemberName == "UriPattern") localPattern = na.TypedValue.Value?.ToString();
                            }
                        }
                    }
                    else if (string.Equals(full, "Koan.Orchestration.Abstractions.Attributes.AppEnvDefaultsAttribute", StringComparison.Ordinal))
                    {
                        if (cad.ConstructorArguments.Count > 0)
                            foreach (var kv in ExtractStringArray(cad.ConstructorArguments[0])) AddKv(appEnv, kv);
                    }
                    else if (string.Equals(full, "Koan.Orchestration.Abstractions.Attributes.HealthEndpointDefaultsAttribute", StringComparison.Ordinal))
                    {
                        if (cad.ConstructorArguments.Count > 0) healthPath = cad.ConstructorArguments[0].Value?.ToString();
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
                    else if (string.Equals(full, "Koan.Orchestration.OrchestrationServiceManifestAttribute", StringComparison.Ordinal))
                    {
                        foreach (var na in cad.NamedArguments)
                        {
                            if (na.MemberName == nameof(OrchestrationServiceManifestAttribute.Type) && na.TypedValue.Value is int et)
                                svcType = (ServiceType)et;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(sid) && !string.IsNullOrWhiteSpace(image))
                {
                    reqs.Add(new ServiceRequirement(sid!, image!, env, ports, volumes.ToArray(), appEnv,
                        svcType,
                        endpointScheme, endpointHost, endpointPattern, localScheme, localHost, localPort, localPattern,
                        healthPath, healthInterval, healthTimeout, healthRetries));
                    added = true;
                }
            }
        }
        catch { }
        return added;
    }

    private sealed class TempAlc : AssemblyLoadContext
    {
        private readonly string _baseDir;
        public TempAlc(string baseDir) : base(isCollectible: true) { _baseDir = baseDir; }
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            try
            {
                var path = Path.Combine(_baseDir, assemblyName.Name + ".dll");
                if (File.Exists(path)) return LoadFromAssemblyPath(path);
            }
            catch { }
            return null;
        }
    }
}

