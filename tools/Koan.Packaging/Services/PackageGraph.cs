using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class PackageGraph
{
    private readonly Dictionary<string, PackageProject> projects;
    private readonly Dictionary<string, IReadOnlyList<string>> dependencies;
    private readonly Dictionary<string, IReadOnlyList<string>> dependents;

    public PackageGraph(IReadOnlyCollection<PackageProject> source)
    {
        projects = source.ToDictionary(project => project.PackageId, StringComparer.OrdinalIgnoreCase);
        if (projects.Count != source.Count)
        {
            throw new InvalidOperationException("Package graph contains duplicate package IDs.");
        }

        var projectByPath = source.ToDictionary(
            FullProjectPath,
            project => project,
            StringComparer.OrdinalIgnoreCase);
        dependencies = source.ToDictionary(
            project => project.PackageId,
            project => (IReadOnlyList<string>)project.ProjectReferences
                .Select(path => projectByPath.GetValueOrDefault(Path.GetFullPath(path))?.PackageId)
                .Where(id => id is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var reverse = source.ToDictionary(
            project => project.PackageId,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var (packageId, packageDependencies) in dependencies)
        {
            foreach (var dependency in packageDependencies) reverse[dependency].Add(packageId);
        }
        dependents = reverse.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        _ = TopologicalOrder(projects.Keys);
    }

    public IReadOnlyCollection<PackageProject> Projects => projects.Values;

    public PackageProject Project(string packageId) =>
        projects.TryGetValue(packageId, out var project)
            ? project
            : throw new InvalidOperationException($"Package graph does not contain '{packageId}'.");

    public IReadOnlyList<string> DependenciesOf(string packageId)
    {
        _ = Project(packageId);
        return dependencies[packageId];
    }

    public IReadOnlyList<string> PackageDependenciesOf(string packageId)
    {
        var project = Project(packageId);
        return project.SuppressDependenciesWhenPacking ? [] : dependencies[packageId];
    }

    public IReadOnlyList<string> ReverseDependentClosure(IEnumerable<string> roots)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            _ = Project(root);
            if (selected.Add(root)) pending.Enqueue(root);
        }

        while (pending.TryDequeue(out var packageId))
        {
            foreach (var dependent in dependents[packageId])
            {
                if (selected.Add(dependent)) pending.Enqueue(dependent);
            }
        }

        return TopologicalOrder(selected);
    }

    public IReadOnlyList<string> TopologicalOrder(IEnumerable<string> packageIds)
    {
        var selected = packageIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(packageId => Project(packageId).PackageId, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(selected.Count);
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Visit(string packageId)
        {
            if (state.TryGetValue(packageId, out var current))
            {
                if (current == 1)
                {
                    throw new InvalidOperationException($"Package dependency cycle contains {packageId}.");
                }
                if (current == 2) return;
            }

            state[packageId] = 1;
            foreach (var dependency in dependencies[packageId])
            {
                if (selected.ContainsKey(dependency)) Visit(dependency);
            }
            state[packageId] = 2;
            result.Add(projects[packageId].PackageId);
        }

        foreach (var packageId in selected.Keys.Order(StringComparer.OrdinalIgnoreCase)) Visit(packageId);
        return result;
    }

    internal static string FullProjectPath(PackageProject project) =>
        Path.GetFullPath(Path.Combine(project.ProjectDirectory, Path.GetFileName(project.ProjectPath)));
}
