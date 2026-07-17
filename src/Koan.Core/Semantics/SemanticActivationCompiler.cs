using System.Collections.Immutable;
using Koan.Core.Composition;
using Koan.Core.Ordering;

namespace Koan.Core.Semantics;

/// <summary>Compiles build evidence and construction-free descriptors into one host constitution.</summary>
internal static class SemanticActivationCompiler
{
    public static SemanticHostConstitution Compile(
        KoanApplicationReferenceManifest manifest,
        IEnumerable<SemanticComponentDescriptor> descriptors,
        System.Reflection.Assembly? applicationAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(descriptors);

        var available = descriptors
            .OrderBy(static descriptor => descriptor.Id)
            .ThenBy(static descriptor => descriptor.ImplementationType.FullName, StringComparer.Ordinal)
            .ToArray();
        var problems = new List<SemanticProblem>();

        var duplicate = available
            .GroupBy(static descriptor => descriptor.Id)
            .FirstOrDefault(static group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            problems.Add(new SemanticProblem(
                duplicate.Key,
                "duplicate-component-id",
                $"Keep one concrete KoanModule in assembly '{duplicate.First().ImplementationType.Assembly.GetName().Name}'."));
            return Invalid(available, problems, isDegraded: !manifest.IsPresent);
        }

        var dependencyEdges = manifest.Dependencies
            .Select(static dependency => (From: new SemanticId(dependency.Owner), To: new SemanticId(dependency.Dependency)))
            .Distinct()
            .OrderBy(static edge => edge.From)
            .ThenBy(static edge => edge.To)
            .ToArray();

        var selfDependency = dependencyEdges.FirstOrDefault(static edge => edge.From == edge.To);
        if (selfDependency != default)
        {
            problems.Add(new SemanticProblem(
                selfDependency.From,
                "self-dependency",
                $"Remove the '{selfDependency.From}' to '{selfDependency.To}' dependency from the declaring project."));
            return Invalid(available, problems, isDegraded: !manifest.IsPresent);
        }

        var dependencyNodes = dependencyEdges.SelectMany(static edge => new[] { edge.From, edge.To }).Distinct().ToArray();
        if (!StableTopologicalOrder.TrySort(
                dependencyNodes,
                dependencyEdges,
                Comparer<SemanticId>.Default,
                out _,
                out var dependencyResidual))
        {
            var dependencyCycleOwner = dependencyResidual[0];
            problems.Add(new SemanticProblem(
                dependencyCycleOwner,
                "dependency-cycle",
                $"Remove one project or package dependency in the cycle containing '{dependencyCycleOwner}'."));
            return Invalid(available, problems, isDegraded: !manifest.IsPresent);
        }

        var evidence = manifest.IsPresent
            ? ReachableFromManifest(manifest, dependencyEdges)
            : available.ToDictionary(
                static descriptor => descriptor.Id,
                static descriptor => new SemanticEvidence(
                    "degraded-fallback",
                    descriptor.ImplementationType.Assembly.GetName().Name ?? descriptor.ImplementationType.Name,
                    ImmutableArray.Create(descriptor.Id)));

        if (applicationAssembly is not null)
        {
            foreach (var descriptor in available.Where(descriptor => descriptor.ImplementationType.Assembly == applicationAssembly))
            {
                evidence.TryAdd(
                    descriptor.Id,
                    new SemanticEvidence(
                        "application-owned",
                        applicationAssembly.GetName().Name ?? descriptor.ImplementationType.Name,
                        ImmutableArray.Create(descriptor.Id)));
            }
        }

        var active = new HashSet<SemanticId>(evidence.Keys);
        var orderedActive = available.Where(descriptor => active.Contains(descriptor.Id)).ToArray();
        var inactive = available.Where(descriptor => !active.Contains(descriptor.Id)).ToArray();
        var decisions = available.Select(descriptor => active.Contains(descriptor.Id)
            ? new SemanticDecision(
                descriptor.Id,
                SemanticDecisionState.Active,
                evidence.TryGetValue(descriptor.Id, out var componentEvidence)
                    ? componentEvidence.Kind
                    : "active",
                componentEvidence)
            : new SemanticDecision(
                descriptor.Id,
                SemanticDecisionState.Inactive,
                "not-declared",
                null));

        return new SemanticHostConstitution(
            orderedActive,
            inactive,
            decisions,
            problems,
            isDegraded: !manifest.IsPresent);
    }

    private static SemanticHostConstitution Invalid(
        IReadOnlyCollection<SemanticComponentDescriptor> available,
        IEnumerable<SemanticProblem> problems,
        bool isDegraded)
    {
        var orderedProblems = problems
            .Distinct()
            .OrderBy(static problem => problem.Owner)
            .ThenBy(static problem => problem.Reason, StringComparer.Ordinal)
            .ToArray();
        var rejectedOwners = orderedProblems
            .Select(static problem => problem.Owner)
            .ToHashSet();
        var rejected = orderedProblems
            .GroupBy(static problem => problem.Owner)
            .Select(static group => group.First())
            .Select(problem => new SemanticDecision(
                problem.Owner,
                SemanticDecisionState.Rejected,
                problem.Reason,
                null));
        var inactive = available
            .Where(descriptor => !rejectedOwners.Contains(descriptor.Id))
            .OrderBy(static descriptor => descriptor.Id)
            .ToArray();
        var inactiveDecisions = inactive.Select(static descriptor => new SemanticDecision(
            descriptor.Id,
            SemanticDecisionState.Inactive,
            "constitution-rejected",
            null));
        return new SemanticHostConstitution(
            [],
            inactive,
            rejected.Concat(inactiveDecisions),
            orderedProblems,
            isDegraded);
    }

    private static Dictionary<SemanticId, SemanticEvidence> ReachableFromManifest(
        KoanApplicationReferenceManifest manifest,
        IReadOnlyCollection<(SemanticId From, SemanticId To)> dependencyEdges)
    {
        var result = new Dictionary<SemanticId, SemanticEvidence>();
        var queue = new Queue<SemanticId>();
        foreach (var reference in manifest.DirectReferences
                     .OrderBy(static reference => reference.CanonicalIdentity, StringComparer.Ordinal))
        {
            var id = new SemanticId(reference.CanonicalIdentity);
            if (!result.TryAdd(id, new SemanticEvidence(
                    reference.Kind == KoanReferenceKind.Package ? "direct-package" : "direct-project",
                    reference.RawIdentity,
                    ImmutableArray.Create(id))))
            {
                continue;
            }

            queue.Enqueue(id);
        }

        var outgoing = dependencyEdges
            .GroupBy(static edge => edge.From)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.To).Order().ToArray());
        while (queue.TryDequeue(out var owner))
        {
            if (!outgoing.TryGetValue(owner, out var members)) continue;
            foreach (var member in members)
            {
                var ownerEvidence = result[owner];
                if (!result.TryAdd(member, new SemanticEvidence(
                        "dependency",
                        owner.ToString(),
                        ownerEvidence.Path.Add(member))))
                {
                    continue;
                }

                queue.Enqueue(member);
            }
        }

        return result;
    }

}
