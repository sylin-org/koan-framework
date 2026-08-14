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

    private async Task<PackageProject?> EvaluateProjectAsync(
        string project,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "dotnet",
            [
                "msbuild", project, "-nologo",
                "-getProperty:IsPackable,PackageId,PackageType,TargetFramework,TargetFrameworks,PackAsTool,IsRoslynComponent,IncludeBuildOutput,SuppressDependenciesWhenPacking,IncludeSymbols,PackageReadmeFile,Description,PackageTags,PackageIcon,PackageProjectUrl,RepositoryUrl,PackageLicenseExpression,PackageReleaseNotes",
                "-getItem:ProjectReference", "-p:PublicRelease=true"
            ],
            repositoryRoot,
            cancellationToken);

        using var document = JsonDocument.Parse(output);
        var properties = document.RootElement.GetProperty("Properties");
        if (!ReadBoolean(properties, "IsPackable", defaultValue: true)) return null;

        var packageId = ReadString(properties, "PackageId");
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException($"Packable project '{Relative(project)}' has no evaluated PackageId.");
        }

        var projectDirectory = Path.GetDirectoryName(project)!;
        var versionOwner = ResolveVersionOwner(project, projectDirectory, packageId);
        if (!string.Equals(versionOwner, ReleaseTrain.FileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Packable package '{packageId}' owned by '{Relative(project)}' resolves versioning from " +
                $"'{versionOwner}'. Remove that nested version.json so every active package inherits " +
                $"the root '{ReleaseTrain.FileName}' release train.");
        }

        var references = new List<string>();
        if (document.RootElement.TryGetProperty("Items", out var items) &&
            items.TryGetProperty("ProjectReference", out var projectReferences))
        {
            foreach (var reference in projectReferences.EnumerateArray())
            {
                var isAnalyzer = string.Equals(
                    ReadString(reference, "OutputItemType"),
                    "Analyzer",
                    StringComparison.OrdinalIgnoreCase);
                if (isAnalyzer)
                {
                    continue;
                }

                if (string.Equals(ReadString(reference, "ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase))
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
            ReadString(properties, "PackageType") ?? "Dependency",
            ReadFrameworks(properties),
            ReadBoolean(properties, "PackAsTool", defaultValue: false),
            ReadBoolean(properties, "IsRoslynComponent", defaultValue: false),
            ReadBoolean(properties, "IncludeBuildOutput", defaultValue: true),
            ReadBoolean(properties, "SuppressDependenciesWhenPacking", defaultValue: false),
            ReadBoolean(properties, "IncludeSymbols", defaultValue: true),
            ReadString(properties, "PackageReadmeFile"),
            HasOwnedReadme(projectDirectory, ReadString(properties, "PackageReadmeFile")),
            RelativeIfExists(Path.Combine(projectDirectory, "TECHNICAL.md")),
            ReadString(properties, "Description") ?? string.Empty,
            ReadString(properties, "PackageTags") ?? string.Empty,
            references,
            ReadString(properties, "PackageIcon"),
            ReadString(properties, "PackageProjectUrl"),
            ReadString(properties, "RepositoryUrl"),
            ReadString(properties, "PackageLicenseExpression"),
            ReadString(properties, "PackageReleaseNotes"));
    }

    private static IReadOnlyList<string> ReadFrameworks(JsonElement properties)
    {
        var value = ReadString(properties, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(value)) value = ReadString(properties, "TargetFramework");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static bool HasOwnedReadme(string projectDirectory, string? readme) =>
        !string.IsNullOrWhiteSpace(readme) && File.Exists(Path.Combine(projectDirectory, readme));

    private string? RelativeIfExists(string path) => File.Exists(path) ? Relative(path) : null;

    private string ResolveVersionOwner(
        string project,
        string projectDirectory,
        string packageId)
    {
        var owner = $"package '{packageId}' owned by '{Relative(project)}'";
        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
        for (var directory = new DirectoryInfo(projectDirectory); directory is not null; directory = directory.Parent)
        {
            var current = directory.FullName.TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase) &&
                !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var versionPath = Path.Combine(current, ReleaseTrain.FileName);
            if (File.Exists(versionPath))
            {
                var relativeVersionPath = Relative(versionPath);
                try
                {
                    _ = ReleaseTrain.ParseJson(File.ReadAllText(versionPath));
                    return relativeVersionPath;
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException)
                {
                    throw new InvalidOperationException(
                        $"Packable {owner} has invalid version configuration at '{relativeVersionPath}': {error.Message}",
                        error);
                }
            }

            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
        }

        throw new InvalidOperationException(
            $"Packable {owner} has no '{ReleaseTrain.FileName}' between its project directory and the repository root. " +
            "Add a version owner for the package or let it inherit the root release train.");
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
