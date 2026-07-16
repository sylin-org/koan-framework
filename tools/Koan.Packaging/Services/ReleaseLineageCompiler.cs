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
        var isBootstrap = string.IsNullOrWhiteSpace(previousLineageRevision);
        ReleaseLineageState? previousState = null;
        IReadOnlyList<ReleaseLineagePackage> previousInventory;
        IReadOnlyList<ReleaseLineagePackage> previousRetirements;
        if (isBootstrap)
        {
            previousSourceCommit = await repository.ResolveCommitAsync(fallbackPreviousSourceRevision, cancellationToken);
            previousVersionCommit = previousSourceCommit;
            await RequireForwardSourceAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await RejectPackageRenamesAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await SwitchBranchAsync(branchName, previousVersionCommit, cancellationToken);
            var previousProjects = await repository.DiscoverPackagesAsync(cancellationToken);
            var bootstrapPreviousVersions = await repository.CalculateVersionsAsync(
                previousProjects,
                previousVersionCommit,
                cancellationToken);
            previousInventory = previousProjects
                .OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(project => new ReleaseLineagePackage(
                    project.PackageId,
                    Normalize(project.ProjectPath),
                    bootstrapPreviousVersions[project.PackageId]))
                .ToArray();
            previousRetirements = [];
            await ApplySourceDeltaAsync(previousSourceCommit, sourceCommit, cancellationToken);
        }
        else
        {
            previousVersionCommit = await repository.ResolveCommitAsync(previousLineageRevision!, cancellationToken);
            previousState = await LoadStateAsync(previousVersionCommit, cancellationToken);
            await ValidateCommittedLineageAsync(repository, previousVersionCommit, previousState, cancellationToken);
            previousSourceCommit = previousState.SourceCommit;
            previousInventory = previousState.Packages;
            previousRetirements = previousState.RetiredPackages;

            if (string.Equals(previousSourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase))
            {
                await SwitchBranchAsync(branchName, previousVersionCommit, cancellationToken);
                return ToCompilation(previousState, previousVersionCommit);
            }

            await RequireForwardSourceAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await RejectPackageRenamesAsync(previousSourceCommit, sourceCommit, cancellationToken);
            await SwitchBranchAsync(branchName, previousVersionCommit, cancellationToken);
            await ApplySourceDeltaAsync(previousSourceCommit, sourceCommit, cancellationToken);
        }

        var projects = await repository.DiscoverPackagesAsync(cancellationToken);
        var graph = new PackageGraph(projects);
        var retiredPackages = ReconcilePackageContinuity(
            previousInventory,
            previousRetirements,
            graph);
        var previousVersions = previousInventory.ToDictionary(
            package => package.PackageId,
            package => package.Version,
            StringComparer.OrdinalIgnoreCase);

        var currentVersionIntents = await ReadCurrentVersionIntentsAsync(
            graph,
            sourceCommit,
            cancellationToken);
        var breakingRoots = isBootstrap
            ? []
            : await FindBreakingRootsAsync(
                graph,
                previousSourceCommit,
                currentVersionIntents,
                cancellationToken);
        var sharedInputsByPackage = await FindChangedSharedInputsAsync(
            graph,
            previousSourceCommit,
            sourceCommit,
            cancellationToken);
        var sharedInputs = sharedInputsByPackage.Values
            .SelectMany(paths => paths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var triggers = PlanTriggers(graph, breakingRoots, sharedInputsByPackage, isBootstrap);
        var closurePackages = triggers.Select(trigger => trigger.PackageId).ToArray();
        var inventory = graph.Projects
            .OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(project => new ReleaseLineagePackage(project.PackageId, Normalize(project.ProjectPath), null))
            .ToList();

        ReleaseLineageState BuildState(
            IEnumerable<string> markerPackages,
            IReadOnlyDictionary<string, string?> versions) => new()
        {
            PreviousSourceCommit = previousSourceCommit,
            SourceCommit = sourceCommit,
            PreviousVersionCommit = previousVersionCommit,
            IsBootstrap = isBootstrap,
            BreakingRoots = breakingRoots.ToList(),
            SharedInputs = sharedInputs.ToList(),
            ClosurePackages = closurePackages.ToList(),
            MarkerPackages = markerPackages.ToList(),
            Triggers = triggers.ToList(),
            Packages = inventory
                .Select(package => package with
                {
                    Version = versions.TryGetValue(package.PackageId, out var version) ? version : null
                })
                .ToList(),
            RetiredPackages = retiredPackages.ToList()
        };

        var state = BuildState([], new Dictionary<string, string?>());
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
        var projectionVersions = await repository.CalculateVersionsAsync(
            graph.Projects,
            projectionCommit,
            cancellationToken);
        var plan = Plan(graph, breakingRoots, sharedInputsByPackage, isBootstrap, previousVersions, projectionVersions);
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
                BreakingRoots = marker.BreakingRoots.ToList(),
                SharedInputs = marker.SharedInputs.ToList()
            };
            await WriteJsonAsync(markerPath, content, cancellationToken);
        }

        state = BuildState(plan.MarkerPackages, new Dictionary<string, string?>());
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

        var markedCommit = await repository.ResolveCommitAsync("HEAD", cancellationToken);
        var markedVersions = await repository.CalculateVersionsAsync(
            graph.Projects,
            markedCommit,
            cancellationToken);
        state = BuildState(plan.MarkerPackages, markedVersions);
        await WriteJsonAsync(
            Path.Combine(repositoryRoot, PackagingConstants.LineageStateFileName),
            state,
            cancellationToken);
        await processRunner.RequireAsync(
            "git",
            ["add", "--", PackagingConstants.LineageStateFileName],
            repositoryRoot,
            cancellationToken);
        await CommitAsync(sourceCommit, amend: true, cancellationToken);

        var versionCommit = await repository.ResolveCommitAsync("HEAD", cancellationToken);
        var finalVersions = await repository.CalculateVersionsAsync(
            graph.Projects,
            versionCommit,
            cancellationToken);
        RequireSameVersions(markedVersions, finalVersions, versionCommit);
        await ValidateCommittedLineageAsync(repository, versionCommit, state, cancellationToken);
        VerifyFreshClosureVersions(
            plan.ClosurePackages,
            previousVersions,
            finalVersions);
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
        await ValidateCommittedLineageAsync(repository, versionCommit, state, cancellationToken);
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
            supplied.IsBootstrap == committed.IsBootstrap &&
            supplied.BreakingRoots.SequenceEqual(committed.BreakingRoots, StringComparer.OrdinalIgnoreCase) &&
            supplied.SharedInputs.SequenceEqual(committed.SharedInputs, StringComparer.OrdinalIgnoreCase) &&
            supplied.ClosurePackages.SequenceEqual(committed.ClosurePackages, StringComparer.OrdinalIgnoreCase) &&
            supplied.MarkerPackages.SequenceEqual(committed.MarkerPackages, StringComparer.OrdinalIgnoreCase) &&
            TriggersEqual(supplied.Triggers, committed.Triggers) &&
            PackagesEqual(supplied.Packages, committed.Packages) &&
            PackagesEqual(supplied.RetiredPackages, committed.RetiredPackages);
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
        IReadOnlyDictionary<string, IReadOnlyList<string>> sharedInputsByPackage,
        bool isBootstrap,
        IReadOnlyDictionary<string, string?> previousVersions,
        IReadOnlyDictionary<string, string?> currentVersions)
    {
        var triggers = PlanTriggers(graph, breakingRoots, sharedInputsByPackage, isBootstrap);
        var closure = triggers.Select(trigger => trigger.PackageId).ToArray();
        var markers = closure.Where(packageId =>
        {
            if (isBootstrap) return true;
            if (!currentVersions.TryGetValue(packageId, out var current) || current is null)
            {
                throw new InvalidOperationException($"No current version was calculated for closure member '{packageId}'.");
            }
            if (!previousVersions.TryGetValue(packageId, out var previous)) return false;
            if (previous is null)
            {
                throw new InvalidOperationException(
                    $"Prior lineage has no durable version identity for existing closure member '{packageId}'.");
            }
            return string.Equals(previous, current, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        return new ReleaseLineagePlan(closure, markers, triggers);
    }

    internal static IReadOnlyList<ReleaseLineagePackage> ReconcilePackageContinuity(
        IEnumerable<ReleaseLineagePackage> previousPackages,
        IEnumerable<ReleaseLineagePackage> previousRetirements,
        PackageGraph current)
    {
        var currentPackages = current.Projects
            .Select(project => new ReleaseLineagePackage(project.PackageId, Normalize(project.ProjectPath), null))
            .ToArray();
        var currentById = currentPackages.ToDictionary(project => project.PackageId, StringComparer.OrdinalIgnoreCase);
        var currentByPath = currentPackages.ToDictionary(project => Normalize(project.ProjectPath), StringComparer.OrdinalIgnoreCase);
        var retired = previousRetirements
            .ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);

        foreach (var prior in retired.Values)
        {
            if (currentById.TryGetValue(prior.PackageId, out var reusedById))
            {
                throw new InvalidOperationException(
                    $"Retired package identity '{prior.PackageId}' cannot be reintroduced at '{reusedById.ProjectPath}'. " +
                    "Package retirement is permanent so an old NuGet identity can never acquire different bits.");
            }
            if (currentByPath.TryGetValue(Normalize(prior.ProjectPath), out var reusedByPath))
            {
                throw new InvalidOperationException(
                    $"Retired package path '{prior.ProjectPath}' cannot be reused by '{reusedByPath.PackageId}'. " +
                    "Choose a new package-owner path so retirement remains unambiguous.");
            }
        }

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
                if (string.IsNullOrWhiteSpace(previous.Version))
                {
                    throw new InvalidOperationException(
                        $"Package '{previous.PackageId}' cannot retire before automatic lineage records its durable identity. " +
                        "Complete the lineage bootstrap before deleting the owner.");
                }
                retired[previous.PackageId] = previous with { ProjectPath = Normalize(previous.ProjectPath) };
                continue;
            }
            if (!string.Equals(Normalize(previous.ProjectPath), Normalize(project.ProjectPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Package rename is not supported by automatic lineage: {previous.PackageId} moved from " +
                    $"'{previous.ProjectPath}' to '{project.ProjectPath}'.");
            }
        }

        return retired.Values
            .OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        IEnumerable<string> breakingRoots,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sharedInputsByPackage,
        bool isBootstrap)
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

        if (isBootstrap || sharedInputsByPackage.Count > 0)
        {
            foreach (var project in graph.Projects)
            {
                if ((isBootstrap || sharedInputsByPackage.ContainsKey(project.PackageId)) &&
                    !triggers.ContainsKey(project.PackageId))
                {
                    triggers[project.PackageId] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        return graph.TopologicalOrder(triggers.Keys)
            .Select(packageId => new ReleaseLineageTrigger(
                packageId,
                triggers[packageId].ToArray(),
                sharedInputsByPackage.TryGetValue(packageId, out var inputs) ? inputs : []))
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> FindBreakingRootsAsync(
        PackageGraph graph,
        string previousSourceCommit,
        IReadOnlyDictionary<string, string> currentVersionIntents,
        CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        foreach (var project in graph.Projects.OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var versionPath = Normalize(Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? string.Empty, "version.json"));
            var previous = await TryReadVersionIntentAsync(previousSourceCommit, versionPath, cancellationToken);
            if (previous is null) continue;
            var current = currentVersionIntents[project.PackageId];
            if (IsBreakingTierAdvance(previous, current)) roots.Add(project.PackageId);
        }
        return roots;
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadCurrentVersionIntentsAsync(
        PackageGraph graph,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        var intents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in graph.Projects.OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var versionPath = Normalize(Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? string.Empty, "version.json"));
            var current = await TryReadVersionIntentAsync(sourceCommit, versionPath, cancellationToken)
                ?? throw new InvalidOperationException($"Package {project.PackageId} has no version intent at {sourceCommit}.");
            _ = ParseVersionIntent(current);
            intents[project.PackageId] = current;
        }
        return intents;
    }

    private static void VerifyFreshClosureVersions(
        IEnumerable<string> closurePackages,
        IReadOnlyDictionary<string, string?> previousVersions,
        IReadOnlyDictionary<string, string?> currentVersions)
    {
        foreach (var packageId in closurePackages)
        {
            previousVersions.TryGetValue(packageId, out var previous);
            var current = currentVersions[packageId]
                ?? throw new InvalidOperationException($"Closure member {packageId} has no final version identity.");
            if (previous is not null && string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Lineage closure member {packageId} retained prior identity {current}; compilation is incomplete.");
            }
        }
    }

    private static void RequireSameVersions(
        IReadOnlyDictionary<string, string?> expected,
        IReadOnlyDictionary<string, string?> actual,
        string versionCommit)
    {
        foreach (var packageId in expected.Keys.Union(actual.Keys, StringComparer.OrdinalIgnoreCase))
        {
            expected.TryGetValue(packageId, out var before);
            actual.TryGetValue(packageId, out var after);
            if (before is not null && string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) continue;
            throw new InvalidOperationException(
                $"Final lineage state changed {packageId}'s calculated identity while amending {versionCommit}: {before} -> {after}.");
        }
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

    private static async Task ValidateCommittedLineageAsync(
        RepositoryInspector repository,
        string versionCommit,
        ReleaseLineageState state,
        CancellationToken cancellationToken)
    {
        var resolvedVersion = await repository.ResolveCommitAsync(versionCommit, cancellationToken);
        var resolvedPrevious = await repository.ResolveCommitAsync(state.PreviousVersionCommit, cancellationToken);
        var resolvedSource = await repository.ResolveCommitAsync(state.SourceCommit, cancellationToken);
        var parents = await repository.GetParentCommitsAsync(resolvedVersion, cancellationToken);
        if (parents.Count != 1 || !Same(parents[0], resolvedPrevious))
        {
            throw new InvalidOperationException(
                $"Lineage commit {resolvedVersion} must have exactly one parent, {resolvedPrevious}; " +
                $"found {(parents.Count == 0 ? "none" : string.Join(", ", parents))}.");
        }

        var resolvedPreviousSource = await repository.ResolveCommitAsync(state.PreviousSourceCommit, cancellationToken);
        ValidateStateShape(state);
        if (state.IsBootstrap)
        {
            if (!Same(resolvedPreviousSource, resolvedPrevious))
            {
                throw new InvalidOperationException(
                    $"Bootstrap lineage {resolvedVersion} must start from its previous source commit {resolvedPreviousSource}; " +
                    $"its parent is {resolvedPrevious}.");
            }
        }
        else
        {
            var previousState = await LoadCommittedStateAsync(repository, resolvedPrevious, cancellationToken);
            if (!Same(previousState.SourceCommit, resolvedPreviousSource))
            {
                throw new InvalidOperationException(
                    $"Lineage source continuity is broken at {resolvedVersion}: previous state records " +
                    $"{previousState.SourceCommit}, current state records {resolvedPreviousSource}.");
            }
            ValidateRetirementContinuity(previousState, state);
        }
        var sourceTree = await repository.ReadTreeAsync(resolvedSource, cancellationToken);
        ValidateReservedSourcePaths(sourceTree.Keys);
        var versionTree = await repository.ReadTreeAsync(resolvedVersion, cancellationToken);
        var sourceProjection = sourceTree
            .Where(entry => !IsGeneratedLineagePath(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var versionProjection = versionTree
            .Where(entry => !IsGeneratedLineagePath(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var mismatch = FirstTreeMismatch(sourceProjection, versionProjection);
        if (mismatch is not null)
        {
            throw new InvalidOperationException(
                $"Lineage commit {resolvedVersion} is not an exact projection of source {resolvedSource}; " +
                $"non-generated path '{mismatch}' differs.");
        }

        var packages = state.Packages.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        var expectedMarkers = state.MarkerPackages.ToDictionary(
            packageId => MarkerPath(packages[packageId]),
            packageId => packageId,
            StringComparer.OrdinalIgnoreCase);
        var actualMarkers = versionTree.Keys
            .Where(IsLineageMarkerPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expectedMarkers.Keys.Where(path => !actualMarkers.Contains(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Lineage commit {resolvedVersion} is missing generated marker(s): {string.Join(", ", missing)}.");
        }

        var parentTree = await repository.ReadTreeAsync(resolvedPrevious, cancellationToken);
        var triggerByPackage = state.Triggers.ToDictionary(trigger => trigger.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (var markerPath in actualMarkers)
        {
            if (!expectedMarkers.TryGetValue(markerPath, out var packageId))
            {
                if (parentTree.TryGetValue(markerPath, out var previousEntry) &&
                    string.Equals(previousEntry, versionTree[markerPath], StringComparison.Ordinal))
                {
                    continue;
                }
                throw new InvalidOperationException(
                    $"Lineage commit {resolvedVersion} contains an unrecorded new or changed marker '{markerPath}'.");
            }

            var content = await repository.TryReadFileAsync(resolvedVersion, markerPath, cancellationToken)
                ?? throw new InvalidOperationException($"Unable to read generated lineage marker '{markerPath}'.");
            var marker = JsonSerializer.Deserialize<ReleaseLineageMarker>(content, JsonOptions)
                ?? throw new InvalidOperationException($"Generated lineage marker '{markerPath}' is empty.");
            var trigger = triggerByPackage[packageId];
            if (marker.SchemaVersion != PackagingConstants.ReleaseLineageSchema ||
                !Same(marker.SourceCommit, state.SourceCommit) ||
                !Same(marker.PackageId, packageId) ||
                !marker.BreakingRoots.SequenceEqual(trigger.BreakingRoots, StringComparer.OrdinalIgnoreCase) ||
                !marker.SharedInputs.SequenceEqual(trigger.SharedInputs, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Generated lineage marker '{markerPath}' does not match committed lineage state.");
            }
        }
    }

    private static void ValidateStateShape(ReleaseLineageState state)
    {
        if (state.Packages.Any(package => string.IsNullOrWhiteSpace(package.Version)) ||
            state.Packages.Select(package => package.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Packages.Count)
        {
            throw new InvalidOperationException(
                "Committed package lineage state must record one durable version identity for every package owner.");
        }
        var packageIds = state.Packages.Select(package => package.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retiredIds = state.RetiredPackages.Select(package => package.PackageId).ToArray();
        var packagePaths = state.Packages.Select(package => Normalize(package.ProjectPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retiredPaths = state.RetiredPackages.Select(package => Normalize(package.ProjectPath)).ToArray();
        if (state.RetiredPackages.Any(package =>
                string.IsNullOrWhiteSpace(package.Version) ||
                string.IsNullOrWhiteSpace(package.ProjectPath)) ||
            retiredIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != retiredIds.Length ||
            retiredPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != retiredPaths.Length ||
            retiredIds.Intersect(packageIds, StringComparer.OrdinalIgnoreCase).Any() ||
            retiredPaths.Intersect(packagePaths, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new InvalidOperationException(
                "Committed package lineage must retain each retired package's final identity and path without reusing it as an active owner.");
        }
        var triggerIds = state.Triggers.Select(trigger => trigger.PackageId).ToArray();
        if (!state.ClosurePackages.SequenceEqual(triggerIds, StringComparer.OrdinalIgnoreCase) ||
            state.MarkerPackages.Except(triggerIds, StringComparer.OrdinalIgnoreCase).Any() ||
            triggerIds.Except(packageIds, StringComparer.OrdinalIgnoreCase).Any() ||
            state.BreakingRoots.Except(triggerIds, StringComparer.OrdinalIgnoreCase).Any() ||
            state.Triggers.SelectMany(trigger => trigger.BreakingRoots)
                .Except(state.BreakingRoots, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new InvalidOperationException("Committed package lineage state has an inconsistent closure or trigger set.");
        }
        if (state.IsBootstrap && !packageIds.SetEquals(triggerIds))
        {
            throw new InvalidOperationException("Bootstrap package lineage must include every package owner.");
        }
        var recordedInputs = state.Triggers.SelectMany(trigger => trigger.SharedInputs)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!recordedInputs.SetEquals(state.SharedInputs))
        {
            throw new InvalidOperationException("Committed package lineage state has an inconsistent shared-input set.");
        }
    }

    private static void ValidateRetirementContinuity(
        ReleaseLineageState previous,
        ReleaseLineageState current)
    {
        var currentActive = current.Packages.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        var expectedRetired = previous.RetiredPackages
            .ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);

        foreach (var package in previous.Packages)
        {
            if (currentActive.TryGetValue(package.PackageId, out var active))
            {
                if (!Same(package.ProjectPath, active.ProjectPath))
                    throw new InvalidOperationException(
                        $"Package rename is not supported by automatic lineage: {package.PackageId} moved from " +
                        $"'{package.ProjectPath}' to '{active.ProjectPath}'.");
                continue;
            }
            expectedRetired[package.PackageId] = package;
        }

        var actualRetired = current.RetiredPackages
            .ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        if (!PackagesEqual(
                expectedRetired.Values.OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase).ToArray(),
                actualRetired.Values.OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase).ToArray()))
        {
            throw new InvalidOperationException(
                "Committed package lineage does not preserve the exact cumulative package-retirement ledger.");
        }
    }

    private static string? FirstTreeMismatch(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        foreach (var path in expected.Keys.Union(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(path, out var expectedEntry) ||
                !actual.TryGetValue(path, out var actualEntry) ||
                !string.Equals(expectedEntry, actualEntry, StringComparison.Ordinal))
            {
                return path;
            }
        }
        return null;
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

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindChangedSharedInputsAsync(
        PackageGraph graph,
        string previousSourceCommit,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        return MapChangedSharedInputs(
            graph,
            await repository.GetChangedPathsAsync(previousSourceCommit, sourceCommit, cancellationToken));
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> MapChangedSharedInputs(
        PackageGraph graph,
        IEnumerable<string> changedPaths)
    {
        var changed = changedPaths.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return graph.Projects
            .Select(project => new
            {
                project.PackageId,
                Inputs = project.SharedInputs
                    .Select(Normalize)
                    .Where(changed.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(item => item.Inputs.Length > 0)
            .ToDictionary(
                item => item.PackageId,
                item => (IReadOnlyList<string>)item.Inputs,
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task RejectPackageRenamesAsync(
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
            if (status.Length == 0 || status[0] != 'R') continue;
            var paths = fields.Skip(1).Where(IsPackageOwnershipPath).ToArray();
            if (paths.Length == 0) continue;
            throw new InvalidOperationException(
                $"Package rename is not supported by automatic lineage: {string.Join(" -> ", paths)}. " +
                "Delete the old owner and introduce a genuinely new package identity/path if replacement is required.");
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
        if (Same(previousSourceCommit, sourceCommit)) return;

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
        IsBootstrap = state.IsBootstrap,
        BreakingRoots = state.BreakingRoots.ToList(),
        SharedInputs = state.SharedInputs.ToList(),
        ClosurePackages = state.ClosurePackages.ToList(),
        MarkerPackages = state.MarkerPackages.ToList(),
        Triggers = state.Triggers.ToList(),
        Packages = state.Packages.ToList(),
        RetiredPackages = state.RetiredPackages.ToList()
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
        var parts = value.Split('.');
        if (parts.Length != 2 ||
            parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)) ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor))
        {
            throw new InvalidOperationException(
                $"Package version intent '{value}' must be exactly unsigned major.minor (for example, 0.18 or 1.0).");
        }
        return new Version(major, minor);
    }

    private static string MarkerPath(PackageProject project) =>
        Normalize(Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? string.Empty, PackagingConstants.LineageMarkerFileName));

    private static string MarkerPath(ReleaseLineagePackage package) =>
        Normalize(Path.Combine(Path.GetDirectoryName(package.ProjectPath) ?? string.Empty, PackagingConstants.LineageMarkerFileName));

    private static bool IsGeneratedLineagePath(string path) =>
        string.Equals(Normalize(path), PackagingConstants.LineageStateFileName, StringComparison.OrdinalIgnoreCase) ||
        IsLineageMarkerPath(path);

    private static bool IsLineageMarkerPath(string path) =>
        string.Equals(Path.GetFileName(Normalize(path)), PackagingConstants.LineageMarkerFileName, StringComparison.OrdinalIgnoreCase);

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
            pair.First.BreakingRoots.SequenceEqual(pair.Second.BreakingRoots, StringComparer.OrdinalIgnoreCase) &&
            pair.First.SharedInputs.SequenceEqual(pair.Second.SharedInputs, StringComparer.OrdinalIgnoreCase));

    private static bool PackagesEqual(
        IReadOnlyList<ReleaseLineagePackage> left,
        IReadOnlyList<ReleaseLineagePackage> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            Same(pair.First.PackageId, pair.Second.PackageId) &&
            Same(pair.First.ProjectPath, pair.Second.ProjectPath) &&
            string.Equals(pair.First.Version, pair.Second.Version, StringComparison.OrdinalIgnoreCase));

    internal sealed record ReleaseLineagePlan(
        IReadOnlyList<string> ClosurePackages,
        IReadOnlyList<string> MarkerPackages,
        IReadOnlyList<ReleaseLineageTrigger> Triggers);
}
