using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using Sora.Data.Core;

namespace Sora.Flow.Model;

// Canonical model base marker; no behavior beyond Entity<T>
public abstract class FlowEntity<TModel> : Entity<TModel> where TModel : FlowEntity<TModel>, new() { }

// Value-object base marker; derives from Entity<T> so standard EntityController<> can be used.
// Unlike FlowEntity<>, value-objects are not treated as canonical roots and should not participate
// in ReferenceItem<> or canonical/lineage projections. They are typically used via StageRecord<TVo>
// and projected into parent-scoped views (e.g., latest/window aggregates).
public abstract class FlowValueObject<TVo> : Entity<TVo> where TVo : FlowValueObject<TVo>, new() { }

// Base interface for DynamicFlowEntity types
public interface IDynamicFlowEntity
{
    ExpandoObject? Model { get; set; }
}

// Normalized transport/delta for a model: Id + dynamic data (JObject or dictionary)
public class DynamicFlowEntity<TModel> : Entity<DynamicFlowEntity<TModel>>, IDynamicFlowEntity
{
    // Nested JSON snapshot using ExpandoObject (document) + primitives/arrays for provider-friendly serialization
    public ExpandoObject? Model { get; set; }
}

// Per-reference policy state persisted alongside root entity
public sealed class PolicyState<TModel> : Entity<PolicyState<TModel>>
{
    // ULID for the target entity (same as Id of ReferenceItem<TModel>)
    [Index]
    public string ReferenceUlid { get; set; } = default!;
    // PolicyName -> SelectedTransformer
    public Dictionary<string, string> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// Stage records per model (hot→processed)
public sealed class StageRecord<TModel> : Entity<StageRecord<TModel>>
{
    [Index]
    public string SourceId { get; set; } = default!;
    [Index]
    public DateTimeOffset OccurredAt { get; set; }
    public string? PolicyVersion { get; set; }
    [Index]
    public string? CorrelationId { get; set; }
    // ULID for the associated canonical entity (set post-Associate)
    [Index]
    public string? ReferenceUlid { get; set; }
    // Use dictionary payload to avoid Mongo discriminator wrappers (_t/_v) on object
    public Dictionary<string, object?>? Data { get; set; }
    // Source metadata (system, adapter) separate from business data
    public Dictionary<string, object?>? Source { get; set; }
}

// Parked intake records (dead-letter with TTL) for later inspection/sweep
public sealed class ParkedRecord<TModel> : Entity<ParkedRecord<TModel>>
{
    [Index]
    public string SourceId { get; set; } = default!;
    [Index]
    public DateTimeOffset OccurredAt { get; set; }
    [Index]
    public string? ReasonCode { get; set; }
    public string? PolicyVersion { get; set; }
    [Index]
    public string? CorrelationId { get; set; }
    // Optional ULID reference for diagnostics
    [Index]
    public string? ReferenceUlid { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
    // Source metadata (system, adapter) separate from business data
    public Dictionary<string, object?>? Source { get; set; }
    public object? Evidence { get; set; }
}

// Aggregation key index per model
public sealed class KeyIndex<TModel> : Entity<KeyIndex<TModel>>
{
    public string AggregationKey { get => Id; set => Id = value; }
    [Index]
    public string ReferenceUlid { get; set; } = default!;
}

// Reference version/projection state per model
public sealed class ReferenceItem<TModel> : Entity<ReferenceItem<TModel>>
{
    // ULID is stored in Id (from Entity<>)
    [Index]
    public ulong Version { get; set; }
    public bool RequiresProjection { get; set; }

}

// Projection task per model
public sealed class ProjectionTask<TModel> : Entity<ProjectionTask<TModel>>
{
    [Index]
    public string? ReferenceUlid { get; set; }
    public ulong Version { get; set; }
    [Index]
    public string ViewName { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Generic view document per model
public class ProjectionView<TModel, TView> : Entity<ProjectionView<TModel, TView>>
{
    [Index]
    public string ViewName { get; set; } = default!;
    // Identifier for filtering and joins
    [Index]
    public string? ReferenceUlid { get; set; }
    public TView? View { get; set; }
}

// Materialized view payload: single value per tag plus policy metadata
// Materialized payload/view types removed in greenfield; materialized snapshot is persisted as the root entity

// Strongly typed canonical and lineage views
// Canonical projection now uses a Model property (nested range-value structure) aligned to root shape
public sealed class CanonicalProjection<TModel> : Entity<CanonicalProjection<TModel>>
{
    [Index]
    public string ViewName { get; set; } = default!;
    // Identifier for filtering and joins
    [Index]
    public string? ReferenceUlid { get; set; }
    public object? Model { get; set; }
}
public sealed class LineageProjection<TModel> : ProjectionView<TModel, Dictionary<string, Dictionary<string, string[]>>> { }
