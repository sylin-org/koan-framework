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
        var expectedClosure = graph.ReverseDependentClosure(lineage.BreakingRoots);
        if (!expectedClosure.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(lineage.ClosurePackages))
        {
            throw new InvalidOperationException(
                "Release lineage closure does not match the current evaluated package graph. Recompile lineage before planning.");
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
        var packages = new List<ReleasePackage>();
        await Parallel.ForEachAsync(
            projects,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (project, ct) =>
            {
                var current = await repository.TryGetVersionAsync(project, versionCommit, ct)
                    ?? throw new InvalidOperationException($"Unable to calculate {project.PackageId} at {versionCommit}.");
                var previous = await repository.TryGetVersionAsync(project, previousVersionCommit, ct);
                var changed = !string.Equals(current, previous, StringComparison.OrdinalIgnoreCase);
                if (offline && !changed) return;
                var published = !offline && await registry.ExistsAsync(project.PackageId, current, ct);
                if (!changed && published) return;

                var dependencies = graph.DependenciesOf(project.PackageId).ToList();
                triggerByPackage.TryGetValue(project.PackageId, out var trigger);
                var reason = trigger is not null
                    ? roots.Contains(project.PackageId)
                        ? PackagingConstants.BreakingRootReason
                        : PackagingConstants.BreakingDependentReason
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
            BreakingRoots = lineage.BreakingRoots.ToList(),
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
