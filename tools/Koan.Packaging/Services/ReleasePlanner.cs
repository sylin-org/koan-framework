using Koan.Packaging.Models;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal sealed class ReleasePlanner(RepositoryInspector repository, NuGetRegistry registry)
{
    public async Task<ReleaseManifest> CreateAsync(
        ReleaseLineage lineage,
        bool offline,
        CancellationToken cancellationToken)
    {
        var previousVersionCommit = await repository.ResolveCommitAsync(lineage.PreviousVersionCommit, cancellationToken);
        var sourceCommit = await repository.ResolveCommitAsync(lineage.SourceCommit, cancellationToken);
        var versionCommit = await repository.ResolveCommitAsync(lineage.VersionCommit, cancellationToken);
        var committedLineage = await ReleaseLineageCompiler.LoadCommittedAsync(
            repository,
            versionCommit,
            cancellationToken);
        ReleaseLineageCompiler.RequireCommittedMatch(lineage, committedLineage);
        var headCommit = await repository.ResolveCommitAsync("HEAD", cancellationToken);
        if (!string.Equals(headCommit, versionCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release planning requires version commit {versionCommit}, but the checkout is {headCommit}.");
        }
        var projects = await repository.DiscoverPackagesAsync(cancellationToken);
        var graph = new PackageGraph(projects);
        var expectedSharedInputs = ReleaseLineageCompiler.MapChangedSharedInputs(
            graph,
            await repository.GetChangedPathsAsync(
                lineage.PreviousSourceCommit,
                lineage.SourceCommit,
                cancellationToken));
        var expectedClosure = graph.ReverseDependentClosure(lineage.BreakingRoots).ToHashSet(StringComparer.OrdinalIgnoreCase);
        expectedClosure.UnionWith(lineage.IsBootstrap
            ? graph.Projects.Select(project => project.PackageId)
            : expectedSharedInputs.Keys);
        if (!expectedClosure.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(lineage.ClosurePackages))
        {
            throw new InvalidOperationException(
                "Release lineage closure does not match the current evaluated package graph. Recompile lineage before planning.");
        }
        var expectedShared = expectedSharedInputs.Values.SelectMany(paths => paths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedShared.SetEquals(lineage.SharedInputs))
        {
            throw new InvalidOperationException(
                "Release lineage shared-input impact does not match the current evaluated package inputs. Recompile lineage before planning.");
        }
        Console.WriteLine(
            $"compare  {previousVersionCommit[..12]} -> {versionCommit[..12]} across {projects.Count} package owner(s); " +
            $"source={sourceCommit[..12]}" +
            (offline ? " (offline)" : " + registry reconciliation"));
        var triggerByPackage = lineage.Triggers.ToDictionary(
            trigger => trigger.PackageId,
            StringComparer.OrdinalIgnoreCase);
        var roots = lineage.BreakingRoots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var markers = lineage.MarkerPackages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentVersions = await repository.CalculateVersionsAsync(
            projects,
            versionCommit,
            cancellationToken);
        var recordedCurrent = lineage.Packages.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        if (!recordedCurrent.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(projects.Select(project => project.PackageId)))
        {
            throw new InvalidOperationException(
                "Committed package lineage inventory does not match the evaluated package graph.");
        }
        foreach (var project in projects)
        {
            var recorded = recordedCurrent[project.PackageId];
            if (!string.Equals(recorded.ProjectPath, project.ProjectPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(recorded.Version, currentVersions[project.PackageId], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Committed package identity for {project.PackageId} does not match exact version commit {versionCommit}.");
            }
        }
        IReadOnlyDictionary<string, string?> previousVersions = lineage.IsBootstrap
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : (await ReleaseLineageCompiler.LoadCommittedAsync(repository, previousVersionCommit, cancellationToken))
                .Packages.ToDictionary(
                    package => package.PackageId,
                    package => package.Version,
                    StringComparer.OrdinalIgnoreCase);
        var packages = new List<ReleasePackage>();
        await Parallel.ForEachAsync(
            projects,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (project, ct) =>
            {
                var current = currentVersions[project.PackageId]
                    ?? throw new InvalidOperationException($"Unable to calculate {project.PackageId} at {versionCommit}.");
                previousVersions.TryGetValue(project.PackageId, out var previous);
                var changed = !string.Equals(current, previous, StringComparison.OrdinalIgnoreCase);
                if (offline && !changed) return;
                var published = !offline && await registry.ExistsAsync(project.PackageId, current, ct);
                if (!changed && published) return;

                var dependencies = graph.DependenciesOf(project.PackageId).ToList();
                triggerByPackage.TryGetValue(project.PackageId, out var trigger);
                var reason = trigger is not null
                    ? roots.Contains(project.PackageId)
                        ? PackagingConstants.BreakingRootReason
                        : trigger.BreakingRoots.Count > 0
                            ? PackagingConstants.BreakingDependentReason
                            : lineage.IsBootstrap
                                ? PackagingConstants.LineageBootstrapReason
                                : PackagingConstants.SharedPackageInputReason
                    : changed
                        ? PackagingConstants.VersionChangedReason
                        : PackagingConstants.RegistryRepairReason;

                lock (packages)
                {
                    packages.Add(new ReleasePackage
                    {
                        PackageId = project.PackageId,
                        Version = current,
                        PreviousVersion = previous,
                        ProjectPath = project.ProjectPath,
                        Kind = project.Kind,
                        Reason = reason,
                        BreakingRoots = trigger?.BreakingRoots.ToList() ?? [],
                        LineageMarkerGenerated = markers.Contains(project.PackageId),
                        AlreadyPublished = published,
                        IncludeSymbols = project.IncludeSymbols,
                        ProjectDependencies = dependencies
                    });
                }
            });

        var ordered = OrderPackages(packages, graph);
        var selected = ordered.Select(package => package.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var omitted = lineage.ClosurePackages.Where(packageId => !selected.Contains(packageId)).ToArray();
        if (omitted.Length > 0)
        {
            throw new InvalidOperationException(
                $"Release plan omitted required reverse-dependent closure member(s): {string.Join(", ", omitted)}.");
        }

        return new ReleaseManifest
        {
            PreviousVersionCommit = previousVersionCommit,
            SourceCommit = sourceCommit,
            VersionCommit = versionCommit,
            IsLineageBootstrap = lineage.IsBootstrap,
            BreakingRoots = lineage.BreakingRoots.ToList(),
            SharedInputs = lineage.SharedInputs.ToList(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Packages = ordered
        };
    }

    private static List<ReleasePackage> OrderPackages(IEnumerable<ReleasePackage> source, PackageGraph graph)
    {
        var packages = source.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        return graph.TopologicalOrder(packages.Keys).Select(packageId => packages[packageId]).ToList();
    }
}
