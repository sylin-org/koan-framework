using Koan.Data.Core;

namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// The single owner of every relational schema decision: which columns a mapping implies, which indexes are
/// worth building, whether the environment consents to automatic DDL, and whether a difference is fatal.
///
/// <para>It works from the mapping plan the adapter's own commands use, never a shape derived separately. A
/// second compiled mapping — the reflection-based compatibility plan this replaced — would have validated a
/// table the adapter neither reads nor writes.</para>
/// </summary>
public interface IRelationalSchemaOrchestrator
{
    RelationalSchemaPlan Plan(MappingPlan mapping, IRelationalStoreFeatures features, RelationalSchemaPolicy policy);

    Task<RelationalSchemaValidation> ValidateAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Brings the store to the shape the mapping needs, or refuses in a way that names the table and the reason.
    /// Returns the validation that stands afterwards.
    /// </summary>
    Task<RelationalSchemaValidation> EnsureCreatedAsync(
        MappingPlan mapping,
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default);
}
