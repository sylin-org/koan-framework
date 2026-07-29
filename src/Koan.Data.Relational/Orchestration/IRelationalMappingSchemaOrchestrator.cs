using Koan.Data.Relational.Orchestration;
using Koan.Data.Core;

namespace Koan.Data.Relational;

/// <summary>Validates or realizes relational shape from the same mapping plan used by commands.</summary>
public interface IRelationalMappingSchemaOrchestrator
{
    RelationalSchemaPlan Plan(MappingPlan mapping, IRelationalStoreFeatures features, RelationalSchemaPolicy policy);
    Task<RelationalSchemaValidation> ValidateAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default);
    Task<RelationalSchemaValidation> EnsureCreatedAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default);
}
