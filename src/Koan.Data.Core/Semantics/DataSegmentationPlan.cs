using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Core.Semantics;

/// <summary>Data-owned shared-row realization of the host's hard segmentation dimensions.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DataSegmentationPlan(SegmentationPlan segmentation) : ISegmentationRealization
{
    private const string ManagedPrefix = "__koan_";
    private static readonly SegmentationRealizationDescriptor Realization = new(
        "data",
        "shared-row",
        [
            "direct.reject",
            "entity.delete",
            "entity.instruction-reject",
            "entity.read",
            "entity.write",
            "vector.filtered"
        ]);
    private readonly ConcurrentDictionary<Type, DataSegmentationScope> _scopes = new();

    public SegmentationRealizationDescriptor SegmentationRealization => Realization;

    public DataSegmentationScope For(Type entityType) => _scopes.GetOrAdd(
        entityType ?? throw new ArgumentNullException(nameof(entityType)),
        static (type, plan) => new DataSegmentationScope(plan.For(type)),
        segmentation);

    public sealed class DataSegmentationScope
    {
        internal static DataSegmentationScope Empty { get; } = new(SegmentationPlan.Empty.Untyped);

        private readonly SegmentationScope _scope;

        internal DataSegmentationScope(SegmentationScope scope)
        {
            _scope = scope;
            Fields = scope.DimensionIds
                .Select(static id => new DataSegmentationField(id, ManagedPrefix + id, typeof(string)))
                .ToImmutableArray();
        }

        public bool IsEmpty => Fields.IsEmpty;

        public ImmutableArray<DataSegmentationField> Fields { get; }

        public DataSegmentationBinding Bind(string operation)
        {
            var bindings = _scope.Bind(operation);
            if (bindings.IsEmpty) return DataSegmentationBinding.Empty;

            var values = new Dictionary<string, object?>(bindings.Length, StringComparer.Ordinal);
            var filters = new Filter[bindings.Length];
            var index = 0;
            foreach (var binding in bindings)
            {
                values.Add(ManagedPrefix + binding.DimensionId, binding.Value);
                filters[index++] = Filter.On(
                    FieldPath.Managed(ManagedPrefix + binding.DimensionId, typeof(string)),
                    FilterOperator.Eq,
                    FilterValue.Of(binding.Value));
            }
            return new DataSegmentationBinding(
                values,
                filters.Length == 1 ? filters[0] : Filter.All(filters));
        }
    }
}

/// <summary>One operation's immutable logical-to-Data binding, evaluated exactly once.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DataSegmentationBinding
{
    internal static DataSegmentationBinding Empty { get; } = new(null, null);

    internal DataSegmentationBinding(IReadOnlyDictionary<string, object?>? values, Filter? readFilter)
    {
        Values = values;
        ReadFilter = readFilter;
    }

    public bool IsEmpty => Values is null;

    public IReadOnlyDictionary<string, object?>? Values { get; }

    public Filter? ReadFilter { get; }
}

/// <summary>One Data-owned physical shared-row field for a logical dimension.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct DataSegmentationField(string DimensionId, string StorageName, Type ClrType);
