using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

/// <summary>
/// PMC-062 (release-model half): a package's compat-band floors are stamped at pack time with the
/// referenced package's version, and its NBGV version advances only when commits touch its own
/// directory. A shared-package fix (e.g. Sylin.Koan.Core) therefore stays unreachable: unchanged
/// dependents keep the old floor forever. This stamper closes that loop mechanically.
///
/// Each packable project owns a <c>dependency-versions.json</c> stamp (inside its directory, so the
/// package's NBGV pathFilters see it). Running the stamper rewrites the stamp whenever a direct or
/// transitive dependency's version moved; the resulting commit is what advances the package's own
/// version, so the next pack re-stamps the floors and the next plan publishes the dependent.
/// Run it repeatedly until it reports no changes, then plan the release — the planner's guard fails
/// any release where a publishing package still has a non-publishing direct dependent.
/// </summary>
internal sealed class DependencyStamper(string repositoryRoot, ProcessRunner processRunner)
{
    private const string StampFileName = "dependency-versions.json";

    public async Task<DependencyStampReport> StampAsync(CancellationToken cancellationToken)
    {
        var packages = (await new RepositoryInspector(repositoryRoot, processRunner)
            .DiscoverPackagesAsync(cancellationToken))
            .OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var byProject = packages.ToDictionary(
            package => Path.GetFullPath(Path.Combine(repositoryRoot, package.ProjectPath)),
            StringComparer.OrdinalIgnoreCase);

        var currentVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentVersions[package.PackageId] = await GetVersionAsync(package, cancellationToken);
        }

        var graph = packages.ToDictionary(
            package => package.PackageId,
            package => package.ProjectReferences
                .Where(reference => byProject.TryGetValue(Path.GetFullPath(reference), out _))
                .Select(reference => byProject[Path.GetFullPath(reference)].PackageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        // Resolve each package's full transitive dependency set once, so the stamp fingerprint covers
        // the whole closure: when anything upstream moves, the fingerprint moves and the package advances.
        var closures = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyCollection<string> Closure(string packageId)
        {
            if (closures.TryGetValue(packageId, out var closed)) return closed;
            var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(packageId);
            while (pending.Count > 0)
            {
                foreach (var dependency in graph[pending.Pop()])
                    if (seen.Add(dependency)) pending.Push(dependency);
            }
            var result = seen.ToArray();
            closures[packageId] = result;
            return result;
        }

        var changed = new List<string>();
        var unchanged = 0;
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stampPath = Path.Combine(repositoryRoot, package.ProjectDirectory, StampFileName);
            var stamp = ReadStamp(stampPath);

            var direct = graph[package.PackageId].ToDictionary(
                id => id,
                id => currentVersions[id],
                StringComparer.OrdinalIgnoreCase);
            var fingerprint = Fingerprint(Closure(package.PackageId)
                .Select(id => (id, currentVersions.GetValueOrDefault(id, "unknown"))));

            if (stamp is not null && StampsMatch(stamp, direct, fingerprint)) { unchanged++; continue; }

            var payload = new Dictionary<string, string>(direct, StringComparer.OrdinalIgnoreCase)
            {
                ["$transitive"] = fingerprint
            };
            var json = JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
            await File.WriteAllTextAsync(stampPath, json + Environment.NewLine, cancellationToken);
            changed.Add(package.PackageId);
        }

        return new DependencyStampReport(changed, unchanged);
    }

    private sealed record Stamp(IReadOnlyDictionary<string, string> Direct, string Fingerprint);

    private static Stamp? ReadStamp(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var document = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (document is null || !document.TryGetValue("$transitive", out var fingerprint)) return null;
            var direct = new Dictionary<string, string>(document, StringComparer.OrdinalIgnoreCase);
            direct.Remove("$transitive");
            return new Stamp(direct, fingerprint);
        }
        catch (JsonException)
        {
            return null;   // a hand-mangled stamp is repaired by rewriting, never fatal
        }
    }

    private static bool StampsMatch(Stamp stamp, IReadOnlyDictionary<string, string> direct, string fingerprint)
    {
        if (!string.Equals(stamp.Fingerprint, fingerprint, StringComparison.Ordinal)) return false;
        if (stamp.Direct.Count != direct.Count) return false;
        foreach (var (id, version) in direct)
            if (!stamp.Direct.TryGetValue(id, out var stamped)
                || !string.Equals(stamped, version, StringComparison.Ordinal)) return false;
        return true;
    }

    private static string Fingerprint(IEnumerable<(string Id, string Version)> entries)
    {
        var material = string.Join("\n", entries
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.Id}|{entry.Version}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private async Task<string> GetVersionAsync(PackageProject package, CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "dotnet",
            ["nbgv", "get-version", "--public-release=true", "-v", "NuGetPackageVersion", "-p", package.ProjectDirectory],
            repositoryRoot,
            cancellationToken);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
    }
}

internal sealed record DependencyStampReport(IReadOnlyList<string> Changed, int Unchanged);
