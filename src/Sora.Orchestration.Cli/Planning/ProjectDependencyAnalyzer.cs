using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Sora.Orchestration.Attributes;
using Sora.Orchestration.Models;
using Sora.Orchestration.Planning;

namespace Sora.Orchestration.Cli.Planning;

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
    /// Discover services declared via [SoraService]/manifest (read-only) by reading generated __SoraOrchestrationManifest.Json.
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
                    var t = asm.GetType("Sora.Orchestration.__SoraOrchestrationManifest", throwOnError: false, ignoreCase: false);
                    if (t is null) continue;
                    var json = t.GetField("Json", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    using var doc = JsonDocument.Parse(json!);
                    if (!doc.RootElement.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var el in services.EnumerateArray())
                    {
                        var id = el.TryGetProperty("shortCode", out var scEl) ? scEl.GetString() : (el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        string? name = el.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                        ServiceType? type = null;
                        if (el.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                        {
                            try { type = (ServiceType)tEl.GetInt32(); } catch { }
                        }
                        if (type is null && el.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.Number)
                        {
                            try { var k = kindEl.GetInt32(); type = k switch { 0 => ServiceType.App, 1 => ServiceType.Database, 2 => ServiceType.Vector, 3 => ServiceType.Ai, _ => ServiceType.Service }; } catch { }
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
                    var t = asm.GetType("Sora.Orchestration.__SoraOrchestrationManifest", throwOnError: false, ignoreCase: false);
                    if (t is null) continue;
                    var json = t.GetField("Json", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    using var doc = JsonDocument.Parse(json!);
                    if (!doc.RootElement.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var el in services.EnumerateArray())
                    {
                        var id = el.TryGetProperty("shortCode", out var scEl) ? scEl.GetString() : (el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var det = new ManifestServiceDetails { Id = id! };
                        if (el.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String) det.Name = nEl.GetString();
                        if (el.TryGetProperty("kind", out var kEl) && kEl.ValueKind == JsonValueKind.Number) det.Kind = kEl.GetInt32();
                        if (el.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                        {
                            try { det.Type = (ServiceType)tEl.GetInt32(); } catch { }
                        }
                        if (el.TryGetProperty("qualifiedCode", out var qcEl) && qcEl.ValueKind == JsonValueKind.String) det.QualifiedCode = qcEl.GetString();
                        if (el.TryGetProperty("containerImage", out var ciEl) && ciEl.ValueKind == JsonValueKind.String) det.ContainerImage = ciEl.GetString();
                        if (el.TryGetProperty("defaultTag", out var dtEl) && dtEl.ValueKind == JsonValueKind.String) det.DefaultTag = dtEl.GetString();
                        if (el.TryGetProperty("defaultPorts", out var dpEl) && dpEl.ValueKind == JsonValueKind.Array) det.DefaultPorts = dpEl.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                        if (el.TryGetProperty("healthEndpoint", out var heEl) && heEl.ValueKind == JsonValueKind.String) det.HealthEndpoint = heEl.GetString();
                        if (el.TryGetProperty("provides", out var prEl) && prEl.ValueKind == JsonValueKind.Array) det.Provides = prEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        if (el.TryGetProperty("consumes", out var coEl) && coEl.ValueKind == JsonValueKind.Array) det.Consumes = coEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        if (el.TryGetProperty("capabilities", out var capEl) && capEl.ValueKind == JsonValueKind.Object)
                        {
                            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                            foreach (var prop in capEl.EnumerateObject()) map[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
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
    /// Discover Sora.* assembly references for the current project (best-effort, read-only).
    /// Returns simple assembly names (e.g., Sora.Data.Postgres, Sora.Web, Sora.Messaging.RabbitMq).
    /// </summary>
    public static List<string>? DiscoverSoraReferences()
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

            // Map to simple names and pick Sora.*
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in closure)
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.StartsWith("Sora.", StringComparison.OrdinalIgnoreCase))
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
                    var t = asm.GetType("Sora.Orchestration.__SoraOrchestrationManifest", throwOnError: false, ignoreCase: false);
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
                            using var doc = JsonDocument.Parse(json!);
                            if (doc.RootElement.TryGetProperty("app", out var appEl) && appEl.ValueKind == JsonValueKind.Object)
                            {
                                includeApp = true;
                                if (appEl.TryGetProperty("defaultPublicPort", out var dpEl) && dpEl.ValueKind == JsonValueKind.Number)
                                    appHttpPort = dpEl.GetInt32();
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Legacy attribute-based fallback (for adapters not yet migrated to ARCH-0049)
            if (reqs.Count == 0)
            {
                foreach (var path in scanDlls)
                {
                    var added = false;
                    try
                    {
                        var asm = mlc.LoadFromAssemblyPath(path);
                        added |= TryAddFromAssemblyAttributes(asm, reqs);
                        added |= TryAddFromTypeAttributes(asm, reqs);
                    }
                    catch { }

                    // Fallback to runtime load of just assembly-level attributes if MLC failed or found nothing
                    if (!added)
                    {
                        try
                        {
                            var alc = new TempAlc(Path.GetDirectoryName(path)!);
                            var asmRt = alc.LoadFromAssemblyPath(path);
                            TryAddFromAssemblyAttributes(asmRt, reqs);
                            alc.Unload();
                        }
                        catch { }
                    }
                }
            }

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
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var el in services.EnumerateArray())
            {
                var id = el.TryGetProperty("shortCode", out var scEl) ? scEl.GetString() : (el.TryGetProperty("id", out var idEl2) ? idEl2.GetString() : null);
                string? image = null;
                if (el.TryGetProperty("containerImage", out var ciEl))
                {
                    var repo = ciEl.GetString();
                    var tag = el.TryGetProperty("defaultTag", out var dtEl) ? dtEl.GetString() : null;
                    image = string.IsNullOrWhiteSpace(tag) ? repo : (repo + ":" + tag);
                }
                image ??= el.TryGetProperty("image", out var imgEl) ? imgEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(image)) continue;

                int[] ports = el.TryGetProperty("defaultPorts", out var dpEl) && dpEl.ValueKind == JsonValueKind.Array
                    ? dpEl.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                    : (el.TryGetProperty("ports", out var pEl) && pEl.ValueKind == JsonValueKind.Array
                        ? pEl.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                        : Array.Empty<int>());
                var env = el.TryGetProperty("env", out var envEl) && envEl.ValueKind == JsonValueKind.Object
                    ? envEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString(), StringComparer.Ordinal)
                    : new Dictionary<string, string?>();
                string[] volumes = el.TryGetProperty("volumes", out var vEl) && vEl.ValueKind == JsonValueKind.Array
                    ? vEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToArray()
                    : Array.Empty<string>();
                var appEnv = el.TryGetProperty("appEnv", out var appEnvEl) && appEnvEl.ValueKind == JsonValueKind.Object
                    ? appEnvEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString(), StringComparer.Ordinal)
                    : new Dictionary<string, string?>();

                string? endpointScheme = el.TryGetProperty("scheme", out var schEl) ? schEl.GetString() : null;
                string? endpointHost = el.TryGetProperty("host", out var hostEl) ? hostEl.GetString() : null;
                string? endpointPattern = el.TryGetProperty("uriPattern", out var upEl) ? upEl.GetString() : null;
                string? localScheme = el.TryGetProperty("localScheme", out var lsEl) ? lsEl.GetString() : null;
                string? localHost = el.TryGetProperty("localHost", out var lhEl) ? lhEl.GetString() : null;
                int? localPort = el.TryGetProperty("localPort", out var lpEl) && lpEl.ValueKind == JsonValueKind.Number ? lpEl.GetInt32() : null;
                string? localPattern = el.TryGetProperty("localPattern", out var lpatEl) ? lpatEl.GetString() : null;
                string? healthPath = el.TryGetProperty("healthEndpoint", out var hepEl) ? hepEl.GetString() : (el.TryGetProperty("healthPath", out var hpEl) ? hpEl.GetString() : null);
                int? healthInterval = el.TryGetProperty("healthInterval", out var hiEl) && hiEl.ValueKind == JsonValueKind.Number ? hiEl.GetInt32() : null;
                int? healthTimeout = el.TryGetProperty("healthTimeout", out var htEl) && htEl.ValueKind == JsonValueKind.Number ? htEl.GetInt32() : null;
                int? healthRetries = el.TryGetProperty("healthRetries", out var hrEl) && hrEl.ValueKind == JsonValueKind.Number ? hrEl.GetInt32() : null;

                ServiceType? type = null;
                if (el.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                {
                    try { type = (ServiceType)tEl.GetInt32(); } catch { }
                }
                if (type is null && el.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.Number)
                {
                    try { var k = kindEl.GetInt32(); type = k switch { 0 => ServiceType.App, 1 => ServiceType.Database, 2 => ServiceType.Vector, 3 => ServiceType.Ai, _ => ServiceType.Service }; } catch { }
                }
                string? name = el.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                string? qCode = el.TryGetProperty("qualifiedCode", out var qcEl) ? qcEl.GetString() : null;
                string? subtype = el.TryGetProperty("subtype", out var stEl) ? stEl.GetString() : null;
                int? deployment = el.TryGetProperty("deploymentKind", out var dkkEl) && dkkEl.ValueKind == JsonValueKind.Number ? dkkEl.GetInt32() : (el.TryGetProperty("deployment", out var dkEl) && dkEl.ValueKind == JsonValueKind.Number ? dkEl.GetInt32() : null);
                string? desc = el.TryGetProperty("description", out var dEl) ? dEl.GetString() : null;
                List<string>? provides = el.TryGetProperty("provides", out var prEl) && prEl.ValueKind == JsonValueKind.Array ? prEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList() : null;
                List<string>? consumes = el.TryGetProperty("consumes", out var coEl) && coEl.ValueKind == JsonValueKind.Array ? coEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList() : null;

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
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("authProviders", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<(string, string, string, string?)>();
            foreach (var el in arr.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var name = el.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                var protocol = el.TryGetProperty("protocol", out var pEl) ? pEl.GetString() : null;
                var icon = el.TryGetProperty("icon", out var iEl) ? iEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    list.Add((id!, name!, protocol ?? string.Empty, icon));
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
                if (!string.Equals(cad.AttributeType.FullName, "Sora.Orchestration.OrchestrationServiceManifestAttribute", StringComparison.Ordinal))
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
                    else if (string.Equals(full, "Sora.Orchestration.Abstractions.Attributes.AppEnvDefaultsAttribute", StringComparison.Ordinal))
                    {
                        if (cad.ConstructorArguments.Count > 0)
                            foreach (var kv in ExtractStringArray(cad.ConstructorArguments[0])) AddKv(appEnv, kv);
                    }
                    else if (string.Equals(full, "Sora.Orchestration.Abstractions.Attributes.HealthEndpointDefaultsAttribute", StringComparison.Ordinal))
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
                    else if (string.Equals(full, "Sora.Orchestration.OrchestrationServiceManifestAttribute", StringComparison.Ordinal))
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
