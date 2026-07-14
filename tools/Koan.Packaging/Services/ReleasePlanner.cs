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
        Console.WriteLine(
            $"compare  {beforeCommit[..12]} -> {sourceCommit[..12]} across {projects.Count} package owner(s)" +
            (offline ? " (offline)" : " + registry reconciliation"));
        var projectByPath = projects.ToDictionary(
            FullProjectPath,
            project => project,
            StringComparer.OrdinalIgnoreCase);

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

                var dependencies = ResolveProjectDependencies(project, projectByPath);

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
            Packages = TopologicalOrder(packages)
        };
    }

    internal static List<string> ResolveProjectDependencies(
        PackageProject project,
        IReadOnlyDictionary<string, PackageProject> projectByPath) =>
        project.ProjectReferences
            .Select(path => projectByPath.GetValueOrDefault(Path.GetFullPath(path))?.PackageId)
            .Where(id => id is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static string FullProjectPath(PackageProject project) =>
        Path.GetFullPath(Path.Combine(project.ProjectDirectory, Path.GetFileName(project.ProjectPath)));

    internal static List<ReleasePackage> TopologicalOrder(IEnumerable<ReleasePackage> source)
    {
        var packages = source.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        var result = new List<ReleasePackage>(packages.Count);
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Visit(ReleasePackage package)
        {
            if (state.TryGetValue(package.PackageId, out var current))
            {
                if (current == 1) throw new InvalidOperationException($"Package dependency cycle contains {package.PackageId}.");
                if (current == 2) return;
            }

            state[package.PackageId] = 1;
            foreach (var dependency in package.ProjectDependencies)
            {
                if (packages.TryGetValue(dependency, out var selectedDependency)) Visit(selectedDependency);
            }
            state[package.PackageId] = 2;
            result.Add(package);
        }

        foreach (var package in packages.Values.OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)) Visit(package);
        return result;
    }
}
