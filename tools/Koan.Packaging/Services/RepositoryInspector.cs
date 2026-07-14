using System.Collections.Concurrent;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class RepositoryInspector(string repositoryRoot, ProcessRunner processRunner)
{
    public async Task<IReadOnlyList<PackageProject>> DiscoverPackagesAsync(CancellationToken cancellationToken)
    {
        var roots = new[] { "src", "packaging" }
            .Select(path => Path.Combine(repositoryRoot, path))
            .Where(Directory.Exists);
        var projects = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Concat(Directory.Exists(Path.Combine(repositoryRoot, "templates"))
                ? Directory.EnumerateFiles(Path.Combine(repositoryRoot, "templates"), "*.csproj", SearchOption.TopDirectoryOnly)
                : [])
            .Where(path => !IsBuildOutput(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new ConcurrentBag<PackageProject>();
        await Parallel.ForEachAsync(
            projects,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = PackagingConstants.EvaluationParallelism,
                CancellationToken = cancellationToken
            },
            async (project, ct) =>
            {
                var package = await EvaluateProjectAsync(project, ct);
                if (package is not null) results.Add(package);
            });

        var packages = results.OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicate = packages.GroupBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Package ID '{duplicate.Key}' is owned by multiple projects: {string.Join(", ", duplicate.Select(item => item.ProjectPath))}");
        }

        return packages;
    }

    public async Task<string> ResolveCommitAsync(string revision, CancellationToken cancellationToken) =>
        await processRunner.RequireAsync("git", ["rev-parse", "--verify", $"{revision}^{{commit}}"], repositoryRoot, cancellationToken);

    public async Task<string?> TryGetVersionAsync(PackageProject package, string commit, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "dotnet",
            ["nbgv", "get-version", commit, "-p", package.ProjectDirectory, "--public-release=true", "--variable", "NuGetPackageVersion"],
            repositoryRoot,
            cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    private async Task<PackageProject?> EvaluateProjectAsync(string project, CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "dotnet",
            [
                "msbuild", project, "-nologo",
                "-getProperty:IsPackable,PackageId,KoanPackageKind,IncludeSymbols,PackageReadmeFile,Description,PackageTags",
                "-getItem:ProjectReference", "-p:PublicRelease=true"
            ],
            repositoryRoot,
            cancellationToken);

        using var document = JsonDocument.Parse(output);
        var properties = document.RootElement.GetProperty("Properties");
        if (!ReadBoolean(properties, "IsPackable", defaultValue: true)) return null;

        var projectDirectory = Path.GetDirectoryName(project)!;
        if (!File.Exists(Path.Combine(projectDirectory, "version.json")))
        {
            throw new InvalidOperationException($"Packable project '{Relative(project)}' has no project-local version.json owner.");
        }

        var packageId = ReadString(properties, "PackageId");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException($"Packable project '{Relative(project)}' has no evaluated PackageId.");
        }

        var references = new List<string>();
        if (document.RootElement.TryGetProperty("Items", out var items) &&
            items.TryGetProperty("ProjectReference", out var projectReferences))
        {
            foreach (var reference in projectReferences.EnumerateArray())
            {
                if (string.Equals(ReadString(reference, "ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ReadString(reference, "OutputItemType"), "Analyzer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fullPath = ReadString(reference, "FullPath");
                if (!string.IsNullOrWhiteSpace(fullPath)) references.Add(Path.GetFullPath(fullPath));
            }
        }

        return new PackageProject(
            Relative(project),
            projectDirectory,
            packageId,
            ReadString(properties, "KoanPackageKind") ?? "Package",
            ReadBoolean(properties, "IncludeSymbols", defaultValue: true),
            ReadString(properties, "PackageReadmeFile"),
            ReadString(properties, "Description") ?? string.Empty,
            ReadString(properties, "PackageTags") ?? string.Empty,
            references);
    }

    private string Relative(string path) => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj");

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;

    private static bool ReadBoolean(JsonElement element, string propertyName, bool defaultValue) =>
        bool.TryParse(ReadString(element, propertyName), out var value) ? value : defaultValue;
}
