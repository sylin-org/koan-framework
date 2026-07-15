using System.Collections.Concurrent;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class ReleaseLineageCompiler(
    string repositoryRoot,
    ProcessRunner processRunner,
    RepositoryInspector repository)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ReleaseLineage> CompileAsync(
        string sourceRevision,
        string fallbackPreviousSourceRevision,
        string branchName,
        string? previousLineageRevision,
        CancellationToken cancellationToken)
    {
        await RequireCleanTrackedTreeAsync(cancellationToken);
        await processRunner.RequireAsync(
            "git",
            ["check-ref-format", "--branch", branchName],
            repositoryRoot,
            cancellationToken);

        var sourceCommit = await repository.ResolveCommitAsync(sourceRevision, cancellationToken);
        ValidateReservedSourcePaths(await ListSourcePathsAsync(sourceCommit, cancellationToken));

        string previousSourceCommit;
        string previousVersionCommit;
        ReleaseLineageState? previousState = null;
        IReadOnlyList<ReleaseLineagePackage> previousInventory;
        if (string.IsNullOrWhiteSpace(previousLineageRevision))
        {
            previousSourceCommit = await repository.ResolveCommitAsync(fallbackPreviousSourceRevision, cancellationToken);
            previousVersionCommit = previousSourceCommit;
            await RequireForwardSourceAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await RejectUnsupportedPackageMovesAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await SwitchBranchAsync(branchName, previousVersionCommit, cancellationToken);
            previousInventory = (await repository.DiscoverPackagesAsync(cancellationToken))
                .OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(project => new ReleaseLineagePackage(project.PackageId, Normalize(project.ProjectPath)))
                .ToArray();
            await ApplySourceDeltaAsync(previousSourceCommit, sourceCommit, cancellationToken);
        }
        else
        {
            previousVersionCommit = await repository.ResolveCommitAsync(previousLineageRevision, cancellationToken);
            previousState = await LoadStateAsync(previousVersionCommit, cancellationToken);
            previousSourceCommit = previousState.SourceCommit;
            previousInventory = previousState.Packages;

            if (string.Equals(previousSourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase))
            {
                await SwitchBranchAsync(branchName, previousVersionCommit, cancellationToken);
                return ToCompilation(previousState, previousVersionCommit);
            }

            await RequireForwardSourceAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await RejectUnsupportedPackageMovesAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await SwitchBranchAsync(branchName, previousVersionCommit, cancellationToken);
            await ApplySourceDeltaAsync(previousSourceCommit, sourceCommit, cancellationToken);
        }

            var projects = await repository.DiscoverPackagesAsync(cancellationToken);
            var graph = new PackageGraph(projects);
            ValidatePackageContinuity(previousInventory, graph);

            var breakingRoots = await FindBreakingRootsAsync(
                graph,
                previousSourceCommit,
                sourceCommit,
                cancellationToken);
            var triggers = PlanTriggers(graph, breakingRoots);
            var closurePackages = graph.ReverseDependentClosure(breakingRoots);
            var inventory = graph.Projects
                .OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(project => new ReleaseLineagePackage(project.PackageId, Normalize(project.ProjectPath)))
                .ToList();

            ReleaseLineageState BuildState(IEnumerable<string> markerPackages) => new()
            {
                PreviousSourceCommit = previousSourceCommit,
                SourceCommit = sourceCommit,
                PreviousVersionCommit = previousVersionCommit,
                BreakingRoots = breakingRoots.ToList(),
                ClosurePackages = closurePackages.ToList(),
                MarkerPackages = markerPackages.ToList(),
                Triggers = triggers.ToList(),
                Packages = inventory
            };

            var state = BuildState([]);
            await WriteJsonAsync(
                Path.Combine(repositoryRoot, PackagingConstants.LineageStateFileName),
                state,
                cancellationToken);
            await processRunner.RequireAsync(
                "git",
                ["add", "--", PackagingConstants.LineageStateFileName],
                repositoryRoot,
                cancellationToken);
            await CommitAsync(sourceCommit, amend: false, cancellationToken);

            var projectionCommit = await repository.ResolveCommitAsync("HEAD", cancellationToken);
            var previousVersions = await CalculateVersionsAsync(
                graph.Projects,
                previousVersionCommit,
                requireAll: false,
                cancellationToken);
            var projectionVersions = await CalculateVersionsAsync(
                graph.Projects,
                projectionCommit,
                requireAll: true,
                cancellationToken);
            var plan = Plan(graph, breakingRoots, previousVersions, projectionVersions);
            var markerIds = plan.MarkerPackages.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var markers = plan.Triggers.Where(trigger => markerIds.Contains(trigger.PackageId)).ToArray();

            foreach (var marker in markers)
            {
                var project = graph.Project(marker.PackageId);
                var markerPath = Path.Combine(project.ProjectDirectory, PackagingConstants.LineageMarkerFileName);
                var content = new ReleaseLineageMarker
                {
                    SourceCommit = sourceCommit,
                    PackageId = marker.PackageId,
                    BreakingRoots = marker.BreakingRoots.ToList()
                };
                await WriteJsonAsync(markerPath, content, cancellationToken);
            }

            state = BuildState(plan.MarkerPackages);
            await WriteJsonAsync(
                Path.Combine(repositoryRoot, PackagingConstants.LineageStateFileName),
                state,
                cancellationToken);

            var generatedPaths = new[] { PackagingConstants.LineageStateFileName }
                .Concat(markers.Select(marker => MarkerPath(graph.Project(marker.PackageId))))
                .ToArray();
            await processRunner.RequireAsync(
                "git",
                new[] { "add", "--" }.Concat(generatedPaths),
                repositoryRoot,
                cancellationToken);
            if (markers.Length > 0) await CommitAsync(sourceCommit, amend: true, cancellationToken);

            var versionCommit = await repository.ResolveCommitAsync("HEAD", cancellationToken);
            await VerifyFreshClosureVersionsAsync(
                graph,
                plan.ClosurePackages,
                previousVersionCommit,
                versionCommit,
                cancellationToken);
            await RequireCleanTrackedTreeAsync(cancellationToken);
            return ToCompilation(state, versionCommit);
    }

    public static async Task SaveAsync(ReleaseLineage lineage, string path, CancellationToken cancellationToken) =>
        await WriteJsonAsync(path, lineage, cancellationToken);

    public static async Task<ReleaseLineage> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var lineage = JsonSerializer.Deserialize<ReleaseLineage>(
            await File.ReadAllTextAsync(path, cancellationToken),
            JsonOptions) ?? throw new InvalidOperationException($"Unable to deserialize release lineage '{path}'.");
        if (lineage.SchemaVersion != PackagingConstants.ReleaseLineageSchema)
        {
            throw new InvalidOperationException(
                $"Release lineage '{path}' uses schema {lineage.SchemaVersion}; expected {PackagingConstants.ReleaseLineageSchema}.");
        }
        return lineage;
    }

    public static async Task<ReleaseLineage> LoadCommittedAsync(
        RepositoryInspector repository,
        string versionCommit,
        CancellationToken cancellationToken)
    {
        var state = await LoadCommittedStateAsync(repository, versionCommit, cancellationToken);
        return ToCompilation(state, versionCommit);
    }

    internal static void RequireCommittedMatch(ReleaseLineage supplied, ReleaseLineage committed)
    {
        var matches =
            supplied.SchemaVersion == committed.SchemaVersion &&
            Same(supplied.PreviousSourceCommit, committed.PreviousSourceCommit) &&
            Same(supplied.SourceCommit, committed.SourceCommit) &&
            Same(supplied.PreviousVersionCommit, committed.PreviousVersionCommit) &&
            Same(supplied.VersionCommit, committed.VersionCommit) &&
            supplied.BreakingRoots.SequenceEqual(committed.BreakingRoots, StringComparer.OrdinalIgnoreCase) &&
            supplied.ClosurePackages.SequenceEqual(committed.ClosurePackages, StringComparer.OrdinalIgnoreCase) &&
            supplied.MarkerPackages.SequenceEqual(committed.MarkerPackages, StringComparer.OrdinalIgnoreCase) &&
            TriggersEqual(supplied.Triggers, committed.Triggers);
        if (matches) return;
        throw new InvalidOperationException(
            $"Release lineage artifact does not match committed state at {committed.VersionCommit}. Recreate it from that version commit.");
    }

    internal static bool IsBreakingTierAdvance(string previous, string current)
    {
        var previousVersion = ParseVersionIntent(previous);
        var currentVersion = ParseVersionIntent(current);
        var previousBand = new Version(previousVersion.Major, previousVersion.Minor);
        var currentBand = new Version(currentVersion.Major, currentVersion.Minor);
        if (currentBand < previousBand)
        {
            throw new InvalidOperationException(
                $"Package version intent cannot move backward from {previous} to {current}.");
        }
        if (currentBand == previousBand) return false;
        return previousVersion.Major == 0
            ? currentVersion.Major != 0 || currentVersion.Minor != previousVersion.Minor
            : currentVersion.Major != previousVersion.Major;
    }

    internal static ReleaseLineagePlan Plan(
        PackageGraph graph,
        IEnumerable<string> breakingRoots,
        IReadOnlyDictionary<string, string?> previousVersions,
        IReadOnlyDictionary<string, string?> currentVersions)
    {
        var triggers = PlanTriggers(graph, breakingRoots);
        var closure = triggers.Select(trigger => trigger.PackageId).ToArray();
        var markers = closure.Where(packageId =>
        {
            if (!currentVersions.TryGetValue(packageId, out var current) || current is null)
            {
                throw new InvalidOperationException($"No current version was calculated for closure member '{packageId}'.");
            }
            return previousVersions.TryGetValue(packageId, out var previous) &&
                   previous is not null &&
                   string.Equals(previous, current, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        return new ReleaseLineagePlan(closure, markers, triggers);
    }

    internal static void ValidatePackageContinuity(
        IEnumerable<ReleaseLineagePackage> previousPackages,
        PackageGraph current) =>
        ValidateInventory(
            previousPackages,
            current.Projects.Select(project => new ReleaseLineagePackage(project.PackageId, Normalize(project.ProjectPath))));

    internal static void ValidateInventory(
        IEnumerable<ReleaseLineagePackage> previousPackages,
        IEnumerable<ReleaseLineagePackage> currentPackages)
    {
        var currentById = currentPackages.ToDictionary(project => project.PackageId, StringComparer.OrdinalIgnoreCase);
        var currentByPath = currentPackages.ToDictionary(project => Normalize(project.ProjectPath), StringComparer.OrdinalIgnoreCase);
        foreach (var previous in previousPackages)
        {
            if (!currentById.TryGetValue(previous.PackageId, out var project))
            {
                if (currentByPath.TryGetValue(Normalize(previous.ProjectPath), out project))
                {
                    throw new InvalidOperationException(
                        $"Package rename is not supported by automatic lineage: '{previous.ProjectPath}' changed ID " +
                        $"from '{previous.PackageId}' to '{project.PackageId}'.");
                }
                throw new InvalidOperationException(
                    $"Package deletion or rename is not supported by automatic lineage: {previous.PackageId} ({previous.ProjectPath}).");
            }
            if (!string.Equals(Normalize(previous.ProjectPath), Normalize(project.ProjectPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Package rename is not supported by automatic lineage: {previous.PackageId} moved from " +
                    $"'{previous.ProjectPath}' to '{project.ProjectPath}'.");
            }
        }
    }

    internal static void ValidateReservedSourcePaths(IEnumerable<string> sourcePaths)
    {
        foreach (var sourcePath in sourcePaths)
        {
            var path = Normalize(sourcePath);
            if (!string.Equals(path, PackagingConstants.LineageStateFileName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetFileName(path), PackagingConstants.LineageMarkerFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            throw new InvalidOperationException(
                $"Source owns reserved automatic-lineage path '{path}'. Remove it from dev.");
        }
    }

    private static IReadOnlyList<ReleaseLineageTrigger> PlanTriggers(
        PackageGraph graph,
        IEnumerable<string> breakingRoots)
    {
        var triggers = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in breakingRoots.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var packageId in graph.ReverseDependentClosure([root]))
            {
                if (!triggers.TryGetValue(packageId, out var packageRoots))
                {
                    packageRoots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    triggers[packageId] = packageRoots;
                }
                packageRoots.Add(root);
            }
        }

        return graph.TopologicalOrder(triggers.Keys)
            .Select(packageId => new ReleaseLineageTrigger(packageId, triggers[packageId].ToArray()))
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> FindBreakingRootsAsync(
        PackageGraph graph,
        string previousSourceCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        foreach (var project in graph.Projects.OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var versionPath = Normalize(Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? string.Empty, "version.json"));
            var previous = await TryReadVersionIntentAsync(previousSourceCommit, versionPath, cancellationToken);
            if (previous is null) continue;
            var current = await TryReadVersionIntentAsync(sourceCommit, versionPath, cancellationToken)
                ?? throw new InvalidOperationException($"Package {project.PackageId} has no version intent at {sourceCommit}.");
            if (IsBreakingTierAdvance(previous, current)) roots.Add(project.PackageId);
        }
        return roots;
    }

    private async Task VerifyFreshClosureVersionsAsync(
        PackageGraph graph,
        IEnumerable<string> closurePackages,
        string previousVersionCommit,
        string versionCommit,
        CancellationToken cancellationToken)
    {
        var projects = closurePackages.Select(graph.Project).ToArray();
        var previousVersions = await CalculateVersionsAsync(
            projects,
            previousVersionCommit,
            requireAll: false,
            cancellationToken);
        var currentVersions = await CalculateVersionsAsync(
            projects,
            versionCommit,
            requireAll: true,
            cancellationToken);
        foreach (var packageId in closurePackages)
        {
            previousVersions.TryGetValue(packageId, out var previous);
            var current = currentVersions[packageId];
            if (previous is not null && string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Breaking closure member {packageId} retained published identity {current}; lineage compilation is incomplete.");
            }
        }
    }

    private async Task<Dictionary<string, string?>> CalculateVersionsAsync(
        IReadOnlyCollection<PackageProject> projects,
        string commit,
        bool requireAll,
        CancellationToken cancellationToken)
    {
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
                var version = await repository.TryGetVersionAsync(project, commit, ct);
                if (requireAll && version is null)
                {
                    throw new InvalidOperationException(
                        $"Unable to calculate {project.PackageId} at lineage commit {commit}.");
                }
                versions[project.PackageId] = version;
            });
        return new Dictionary<string, string?>(versions, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ReleaseLineageState> LoadStateAsync(string commit, CancellationToken cancellationToken)
    {
        return await LoadCommittedStateAsync(repository, commit, cancellationToken);
    }

    private static async Task<ReleaseLineageState> LoadCommittedStateAsync(
        RepositoryInspector repository,
        string commit,
        CancellationToken cancellationToken)
    {
        var content = await repository.TryReadFileAsync(commit, PackagingConstants.LineageStateFileName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Lineage commit {commit} has no {PackagingConstants.LineageStateFileName}; refuse to infer prior release state.");
        var state = JsonSerializer.Deserialize<ReleaseLineageState>(content, JsonOptions)
            ?? throw new InvalidOperationException($"Lineage state at {commit} is empty.");
        if (state.SchemaVersion != PackagingConstants.ReleaseLineageSchema)
        {
            throw new InvalidOperationException(
                $"Lineage state at {commit} uses schema {state.SchemaVersion}; expected {PackagingConstants.ReleaseLineageSchema}.");
        }
        return state;
    }

    private async Task<string?> TryReadVersionIntentAsync(
        string commit,
        string path,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "git",
            ["show", $"{commit}:{path}"],
            repositoryRoot,
            cancellationToken);
        if (result.ExitCode != 0) return null;
        using var document = JsonDocument.Parse(result.StandardOutput);
        if (!document.RootElement.TryGetProperty("version", out var version) ||
            string.IsNullOrWhiteSpace(version.GetString()))
        {
            throw new InvalidOperationException($"Package version intent '{path}' at {commit} has no version.");
        }
        return version.GetString();
    }

    private async Task<IReadOnlyList<string>> ListSourcePathsAsync(
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "git",
            ["ls-tree", "-r", "--name-only", sourceCommit],
            repositoryRoot,
            cancellationToken);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task RejectUnsupportedPackageMovesAsync(
        string previousSourceCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "git",
            ["diff", "--name-status", "--find-renames", previousSourceCommit, sourceCommit],
            repositoryRoot,
            cancellationToken);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            var status = fields[0];
            if (status.Length == 0 || status[0] is not ('D' or 'R')) continue;
            var paths = fields.Skip(1).Where(IsPackageOwnershipPath).ToArray();
            if (paths.Length == 0) continue;
            throw new InvalidOperationException(
                $"Package deletion or rename is not supported by automatic lineage: {string.Join(" -> ", paths)}.");
        }
    }

    private async Task RequireForwardSourceAsync(
        string previousSourceCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "git",
            ["merge-base", "--is-ancestor", previousSourceCommit, sourceCommit],
            repositoryRoot,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Source {sourceCommit} does not advance previous lineage source {previousSourceCommit}; force-pushed/non-forward history is unsupported.");
        }
    }

    private async Task RequireCleanTrackedTreeAsync(CancellationToken cancellationToken)
    {
        var status = await processRunner.RequireAsync(
            "git",
            ["status", "--porcelain", "--untracked-files=no"],
            repositoryRoot,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException("Package lineage requires a checkout with no tracked modifications.");
        }
    }

    private Task SwitchBranchAsync(string branchName, string commit, CancellationToken cancellationToken) =>
        processRunner.RequireAsync(
            "git",
            ["switch", "--force-create", branchName, commit],
            repositoryRoot,
            cancellationToken,
            echo: true);

    private async Task ApplySourceDeltaAsync(
        string previousSourceCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        var patchPath = Path.Combine(Path.GetTempPath(), $"koan-package-lineage-{Guid.NewGuid():N}.patch");
        try
        {
            await processRunner.RequireAsync(
                "git",
                [
                    "diff", "--binary", "--full-index", $"--output={patchPath}",
                    previousSourceCommit, sourceCommit, "--"
                ],
                repositoryRoot,
                cancellationToken);
            var apply = await processRunner.RunAsync(
                "git",
                ["apply", "--index", patchPath],
                repositoryRoot,
                cancellationToken,
                echo: true);
            if (apply.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Unable to project source delta {previousSourceCommit}..{sourceCommit} onto the package lineage. " +
                    $"Resolve the history conflict before advancing dev.{Environment.NewLine}{apply.StandardError}{apply.StandardOutput}");
            }
        }
        finally
        {
            try { File.Delete(patchPath); } catch { }
        }
    }

    private async Task CommitAsync(string sourceCommit, bool amend, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-c", $"user.name={PackagingConstants.LineageCommitterName}",
            "-c", $"user.email={PackagingConstants.LineageCommitterEmail}",
            "commit", "--no-gpg-sign"
        };
        if (amend)
        {
            arguments.Add("--amend");
            arguments.Add("--no-edit");
        }
        else
        {
            arguments.Add("-m");
            arguments.Add($"release: compile package lineage for {sourceCommit[..12]}");
        }
        await processRunner.RequireAsync(
            "git",
            arguments,
            repositoryRoot,
            cancellationToken,
            echo: true);
    }

    private static ReleaseLineage ToCompilation(ReleaseLineageState state, string versionCommit) => new()
    {
        PreviousSourceCommit = state.PreviousSourceCommit,
        SourceCommit = state.SourceCommit,
        PreviousVersionCommit = state.PreviousVersionCommit,
        VersionCommit = versionCommit,
        BreakingRoots = state.BreakingRoots.ToList(),
        ClosurePackages = state.ClosurePackages.ToList(),
        MarkerPackages = state.MarkerPackages.ToList(),
        Triggers = state.Triggers.ToList()
    };

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine,
            cancellationToken);
    }

    private static Version ParseVersionIntent(string value)
    {
        var numeric = value.Split(['-', '+'], 2)[0];
        if (!Version.TryParse(numeric, out var version) || version.Minor < 0)
        {
            throw new InvalidOperationException($"Package version intent '{value}' is not a semantic major.minor value.");
        }
        return version;
    }

    private static string MarkerPath(PackageProject project) =>
        Normalize(Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? string.Empty, PackagingConstants.LineageMarkerFileName));

    private static bool IsPackageOwnershipPath(string path)
    {
        var normalized = Normalize(path);
        var inPackageRoot = normalized.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
                            normalized.StartsWith("packaging/", StringComparison.OrdinalIgnoreCase) ||
                            normalized.StartsWith("templates/", StringComparison.OrdinalIgnoreCase);
        return inPackageRoot &&
               (normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("/version.json", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool TriggersEqual(
        IReadOnlyList<ReleaseLineageTrigger> left,
        IReadOnlyList<ReleaseLineageTrigger> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            Same(pair.First.PackageId, pair.Second.PackageId) &&
            pair.First.BreakingRoots.SequenceEqual(pair.Second.BreakingRoots, StringComparer.OrdinalIgnoreCase));

    internal sealed record ReleaseLineagePlan(
        IReadOnlyList<string> ClosurePackages,
        IReadOnlyList<string> MarkerPackages,
        IReadOnlyList<ReleaseLineageTrigger> Triggers);
}
