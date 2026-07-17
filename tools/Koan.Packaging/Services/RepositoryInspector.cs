using System.Collections.Concurrent;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class RepositoryInspector(string repositoryRoot, ProcessRunner processRunner)
{
    public async Task<IReadOnlyList<PackageProject>> DiscoverPackagesAsync(CancellationToken cancellationToken)
    {
        var snapshot = new PackageDiscoverySnapshot(ReadTrackedRepositoryFilesAsync(cancellationToken));
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
                var package = await EvaluateProjectAsync(project, snapshot, ct);
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

    public async Task<IReadOnlyList<string>> GetParentCommitsAsync(
        string commit,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "git",
            ["rev-list", "--parents", "-n", "1", commit],
            repositoryRoot,
            cancellationToken);
        return output.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadTreeAsync(
        string commit,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "git",
            ["ls-tree", "-r", "--full-tree", commit],
            repositoryRoot,
            cancellationToken);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2))
            .ToDictionary(
                fields => fields[1].Replace('\\', '/'),
                fields => fields[0],
                StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> GetChangedPathsAsync(
        string previousCommit,
        string currentCommit,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "git",
            ["diff", "--name-only", "--no-renames", previousCommit, currentCommit, "--"],
            repositoryRoot,
            cancellationToken);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, string?>> CalculateVersionsAsync(
        IReadOnlyCollection<PackageProject> projects,
        string commit,
        CancellationToken cancellationToken)
    {
        var resolvedCommit = await ResolveCommitAsync(commit, cancellationToken);
        var head = await ResolveCommitAsync("HEAD", cancellationToken);
        if (!string.Equals(resolvedCommit, head, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Version calculation requires exact commit {resolvedCommit} to be checked out; HEAD is {head}.");
        }

        var versions = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(
            projects,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = PackagingConstants.EvaluationParallelism,
                CancellationToken = cancellationToken
            },
            async (project, ct) =>
            {
                var result = await processRunner.RunAsync(
                    "dotnet",
                    [
                        "nbgv", "get-version", "HEAD", "-p", project.ProjectDirectory,
                        "--public-release=true", "--variable", "NuGetPackageVersion"
                    ],
                    repositoryRoot,
                    ct);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Unable to calculate {project.PackageId} at exact commit {resolvedCommit}: " +
                        $"{result.StandardError}{result.StandardOutput}".Trim());
                }
                versions[project.PackageId] = result.StandardOutput.Trim();
            });
        return new Dictionary<string, string?>(versions, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> TryReadFileAsync(string commit, string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "git",
            ["show", $"{commit}:{repositoryPath.Replace('\\', '/')}"],
            repositoryRoot,
            cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput : null;
    }

    private async Task<PackageProject?> EvaluateProjectAsync(
        string project,
        PackageDiscoverySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "dotnet",
            [
                "msbuild", project, "-nologo",
                "-getProperty:IsPackable,PackageId,PackageType,TargetFramework,TargetFrameworks,PackAsTool,IsRoslynComponent,IncludeBuildOutput,SuppressDependenciesWhenPacking,IncludeSymbols,PackageReadmeFile,Description,PackageTags,PackageIcon,PackageProjectUrl,RepositoryUrl,PackageLicenseExpression,PackageReleaseNotes",
                $"-getItem:ProjectReference,None,{PackagingConstants.PackageInputItemName}", "-p:PublicRelease=true"
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
        ValidateVersionIntent(project, projectDirectory, packageId);

        var references = new List<string>();
        var sharedInputs = SharedBuildInputs(projectDirectory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalPayloadsWithoutSourceOwnership = new List<string>();
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
                    var analyzerProject = ReadString(reference, "FullPath");
                    foreach (var input in await SourceProjectInputsAsync(
                                 snapshot,
                                 analyzerProject,
                                 packageId,
                                 cancellationToken))
                        sharedInputs.Add(input);
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
        if (document.RootElement.TryGetProperty("Items", out items) &&
            items.TryGetProperty("None", out var noneItems))
        {
            foreach (var item in noneItems.EnumerateArray())
            {
                if (!ReadBoolean(item, "Pack", defaultValue: false)) continue;
                var fullPath = ReadString(item, "FullPath");
                if (string.IsNullOrWhiteSpace(fullPath) || IsWithin(fullPath, projectDirectory)) continue;
                if (!TryRelative(fullPath, out var relative))
                {
                    throw new InvalidOperationException(
                        $"Package '{packageId}' packs external file '{fullPath}' from outside the versioned " +
                        "repository. Package payloads must be reproducible from repository-owned inputs; move " +
                        "the file into the repository and declare its tracked source ownership.");
                }
                if (IsBuildOutput(fullPath))
                {
                    externalPayloadsWithoutSourceOwnership.Add(relative);
                    continue;
                }

                var repositoryFiles = await snapshot.TrackedRepositoryFiles;
                if (repositoryFiles.Contains(relative))
                {
                    sharedInputs.Add(relative);
                    continue;
                }

                externalPayloadsWithoutSourceOwnership.Add(relative);
            }
        }
        var declaredPackageInputCount = 0;
        if (document.RootElement.TryGetProperty("Items", out items) &&
            items.TryGetProperty(PackagingConstants.PackageInputItemName, out var packageInputs))
        {
            foreach (var item in packageInputs.EnumerateArray())
            {
                var fullPath = ReadString(item, "FullPath");
                if (string.IsNullOrWhiteSpace(fullPath) ||
                    !File.Exists(fullPath) ||
                    IsWithin(fullPath, projectDirectory) ||
                    !TryRelative(fullPath, out var relative) ||
                    IsBuildOutput(fullPath))
                {
                    throw new InvalidOperationException(
                        $"Package '{packageId}' has an invalid {PackagingConstants.PackageInputItemName} " +
                        $"'{ReadString(item, "Identity") ?? fullPath ?? "<empty>"}'. Declare a tracked source file " +
                        "outside the owning project and inside the repository; local files already belong to the " +
                        "owner, and generated bin/obj output cannot own package version impact.");
                }
                var repositoryFiles = await snapshot.TrackedRepositoryFiles;
                if (!repositoryFiles.Contains(relative))
                {
                    throw new InvalidOperationException(
                        $"Package '{packageId}' declares {PackagingConstants.PackageInputItemName} '{relative}', " +
                        "but that source is not tracked by Git. Commit the real source input; ignored or local " +
                        "artifacts cannot own package version impact.");
                }

                sharedInputs.Add(relative);
                declaredPackageInputCount++;
            }
        }
        if (externalPayloadsWithoutSourceOwnership.Count > 0 && declaredPackageInputCount == 0)
        {
            throw new InvalidOperationException(
                $"Package '{packageId}' packs generated, ignored, or untracked external payload " +
                $"'{externalPayloadsWithoutSourceOwnership[0]}' but declares no " +
                $"{PackagingConstants.PackageInputItemName}. Declare the payload's tracked source inputs so every " +
                "payload change mints a fresh owning package version.");
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
            sharedInputs.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
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

    private void ValidateVersionIntent(string project, string projectDirectory, string packageId)
    {
        var versionPath = Path.Combine(projectDirectory, VersionIntent.FileName);
        var relativeVersionPath = Relative(versionPath);
        var owner = $"package '{packageId}' owned by '{Relative(project)}'";
        if (!File.Exists(versionPath))
        {
            throw new InvalidOperationException(
                $"Packable {owner} has no project-local version intent at '{relativeVersionPath}'. " +
                $"Add '{relativeVersionPath}' with a 'version' property set to exactly {VersionIntent.RequiredFormat}; " +
                "NBGV owns patch versions.");
        }

        try
        {
            _ = VersionIntent.ParseJson(File.ReadAllText(versionPath));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Packable {owner} has invalid version intent at '{relativeVersionPath}': {error.Message} " +
                $"Set its 'version' property to exactly {VersionIntent.RequiredFormat}; NBGV owns patch versions.",
                error);
        }
    }

    private IEnumerable<string> SharedBuildInputs(string projectDirectory)
    {
        string[] repositoryWide =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "Directory.Packages.targets",
            "global.json",
            ".editorconfig",
            ".config/dotnet-tools.json",
            "NuGet.Config",
            "README.md",
            "icon.png",
            "build/compat-ranges.targets"
        ];
        foreach (var path in repositoryWide) yield return path;

        var directory = new DirectoryInfo(projectDirectory).Parent;
        while (directory is not null && IsWithin(directory.FullName, repositoryRoot))
        {
            if (!string.Equals(directory.FullName, repositoryRoot, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var fileName in new[]
                         {
                             "Directory.Build.props",
                             "Directory.Build.targets",
                             "Directory.Packages.props",
                             "Directory.Packages.targets"
                         })
                {
                    yield return Relative(Path.Combine(directory.FullName, fileName));
                }
            }
            directory = directory.Parent;
        }
    }

    private Task<IReadOnlyList<string>> SourceProjectInputsAsync(
        PackageDiscoverySnapshot snapshot,
        string? project,
        string packageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project) ||
            !File.Exists(project) ||
            !TryRelative(project, out _))
        {
            throw new InvalidOperationException(
                $"Package '{packageId}' references an analyzer project outside the versioned repository. " +
                "Keep source-generating build inputs inside the repository so package impact remains automatic.");
        }

        var fullProject = Path.GetFullPath(project);
        return snapshot.AnalyzerSourceInputs.GetOrAdd(
            fullProject,
            _ => ReadTrackedProjectInputsAsync(snapshot, fullProject, packageId, cancellationToken));
    }

    private async Task<IReadOnlyList<string>> ReadTrackedProjectInputsAsync(
        PackageDiscoverySnapshot snapshot,
        string project,
        string packageId,
        CancellationToken cancellationToken)
    {
        var projectDirectory = Relative(Path.GetDirectoryName(project)!).TrimEnd('/') + "/";
        var repositoryFiles = await snapshot.TrackedRepositoryFiles;
        var inputs = repositoryFiles
            .Where(path => path.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(Path.Combine(repositoryRoot, path)))
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                PackagingConstants.LineageMarkerFileName,
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var projectPath = Relative(project);
        if (!inputs.Contains(projectPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Package '{packageId}' references analyzer project '{projectPath}', but that project is not " +
                "tracked by Git. Commit the analyzer source so package impact can be calculated deterministically.");
        }

        return inputs;
    }

    private async Task<IReadOnlySet<string>> ReadTrackedRepositoryFilesAsync(CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "git",
            ["ls-files"],
            repositoryRoot,
            cancellationToken);
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => path.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool TryRelative(string path, out string relative)
    {
        relative = string.Empty;
        if (!IsWithin(path, repositoryRoot)) return false;
        relative = Relative(path);
        return true;
    }

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private string Relative(string path) => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj");

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;

    private static bool ReadBoolean(JsonElement element, string propertyName, bool defaultValue) =>
        bool.TryParse(ReadString(element, propertyName), out var value) ? value : defaultValue;

    private sealed class PackageDiscoverySnapshot(Task<IReadOnlySet<string>> trackedRepositoryFiles)
    {
        public Task<IReadOnlySet<string>> TrackedRepositoryFiles { get; } = trackedRepositoryFiles;
        public ConcurrentDictionary<string, Task<IReadOnlyList<string>>> AnalyzerSourceInputs { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
