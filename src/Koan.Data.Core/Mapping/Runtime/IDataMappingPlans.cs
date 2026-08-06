using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>Host-owned access to frozen, bounded, warm-reused mapping plans.</summary>
public interface IDataMappingPlans
{
    MappingPlan? Find(string source, Type entityType);
    MappingPlan? Find<TEntity>(string source);
    MappingPlan Require(string source, Type entityType);
    MappingPlan Require<TEntity>(string source);
    MappingPlan GetOrAdd(string source, Type entityType, MappingConvention convention);
    MappingPlan GetOrAdd<TEntity>(string source, MappingConvention convention);
    IReadOnlyList<MappingPlan> Snapshot();
}
