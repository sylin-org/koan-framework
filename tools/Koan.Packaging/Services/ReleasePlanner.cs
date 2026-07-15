using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class ReleasePlanner(RepositoryInspector repository, NuGetRegistry registry)
{
    public async Task<ReleaseManifest> CreateAsync(
        string beforeRevision,
        string afterRevision,
        bool offline,
        CancellationToken cancellationToken)
    {
        var beforeCommit = await repository.ResolveCommitAsync(beforeRevision, cancellationToken);
        var sourceCommit = await repository.ResolveCommitAsync(afterRevision, cancellationToken);
        var projects = await repository.DiscoverPackagesAsync(cancellationToken);
        var graph = new PackageGraph(projects);
        Console.WriteLine(
            $"compare  {beforeCommit[..12]} -> {sourceCommit[..12]} across {projects.Count} package owner(s)" +
            (offline ? " (offline)" : " + registry reconciliation"));
        var packages = new List<ReleasePackage>();
        await Parallel.ForEachAsync(
            projects,
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (project, ct) =>
            {
                var current = await repository.TryGetVersionAsync(project, sourceCommit, ct)
                    ?? throw new InvalidOperationException($"Unable to calculate {project.PackageId} at {sourceCommit}.");
                var previous = await repository.TryGetVersionAsync(project, beforeCommit, ct);
                var changed = !string.Equals(current, previous, StringComparison.OrdinalIgnoreCase);
                if (offline && !changed) return;
                var published = !offline && await registry.ExistsAsync(project.PackageId, current, ct);
                if (!changed && published) return;

                var dependencies = graph.DependenciesOf(project.PackageId).ToList();

                lock (packages)
                {
                    packages.Add(new ReleasePackage
                    {
                        PackageId = project.PackageId,
                        Version = current,
                        PreviousVersion = previous,
                        ProjectPath = project.ProjectPath,
                        Kind = project.Kind,
                        Reason = changed ? "version-changed" : "unpublished-current-version",
                        AlreadyPublished = published,
                        IncludeSymbols = project.IncludeSymbols,
                        ProjectDependencies = dependencies
                    });
                }
            });

        return new ReleaseManifest
        {
            BeforeCommit = beforeCommit,
            SourceCommit = sourceCommit,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Packages = OrderPackages(packages, graph)
        };
    }

    private static List<ReleasePackage> OrderPackages(IEnumerable<ReleasePackage> source, PackageGraph graph)
    {
        var packages = source.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        return graph.TopologicalOrder(packages.Keys).Select(packageId => packages[packageId]).ToList();
    }
}
