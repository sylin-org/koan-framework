using System.Collections.Concurrent;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// The host-owned compiler for round-trip stored-field transforms. Contributors are fixed by DI composition and each
/// Entity-type plan is built once; runtime operations consume only the immutable compiled plan.
/// </summary>
internal sealed class StorageFieldTransformPlan : IFieldTransformInspector
{
    private readonly IFieldTransformContributor[] _contributors;
    private readonly ConcurrentDictionary<Type, Compiled> _plans = new();

    public StorageFieldTransformPlan(IEnumerable<IFieldTransformContributor> contributors)
    {
        _contributors = contributors
            .OrderBy(contributor => contributor.Order)
            .ThenBy(contributor => contributor.Id, StringComparer.Ordinal)
            .ToArray();

        var duplicate = _contributors
            .GroupBy(contributor => contributor.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Field-transform contributor id '{duplicate.Key}' is registered more than once.");
    }

    public Compiled For(Type entityType)
        => _plans.GetOrAdd(
            entityType ?? throw new ArgumentNullException(nameof(entityType)),
            Build);

    public bool HasTransformsFor(Type entityType) => For(entityType).HasTransforms;

    public IReadOnlyList<string> ContributorIdsFor(Type entityType) => For(entityType).ContributorIds;

    private Compiled Build(Type entityType)
    {
        if (_contributors.Length == 0) return Compiled.Empty;

        var transforms = new List<IFieldTransform>(_contributors.Length);
        var ids = new List<string>(_contributors.Length);
        foreach (var contributor in _contributors)
        {
            var transform = contributor.Build(entityType);
            if (transform is null) continue;
            transforms.Add(transform);
            ids.Add(contributor.Id);
        }

        return transforms.Count == 0
            ? Compiled.Empty
            : new Compiled(transforms.ToArray(), ids.ToArray());
    }

    internal sealed class Compiled
    {
        internal static Compiled Empty { get; } = new([], []);

        private readonly IFieldTransform[] _transforms;

        internal Compiled(IFieldTransform[] transforms, IReadOnlyList<string> contributorIds)
        {
            _transforms = transforms;
            ContributorIds = contributorIds;
        }

        public bool HasTransforms => _transforms.Length > 0;

        public IReadOnlyList<string> ContributorIds { get; }

        public object CloneForWrite(object entity)
        {
            var clone = EntityCloner.ShallowClone(entity);
            foreach (var transform in _transforms) transform.ApplyOnWrite(clone);
            return clone;
        }

        public void ApplyOnRead(object entity)
        {
            for (var index = _transforms.Length - 1; index >= 0; index--)
                _transforms[index].ApplyOnRead(entity);
        }
    }
}
