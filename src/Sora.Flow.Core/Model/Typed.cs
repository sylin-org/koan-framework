using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;
using System.Collections.Generic;
using System.Dynamic;

namespace Sora.Flow.Model;

// Canonical model base marker; no behavior beyond Entity<T>
public abstract class FlowEntity<TModel> : Entity<TModel> where TModel : FlowEntity<TModel>, new() { }

// Normalized transport/delta for a model: Id + dynamic data (JObject or dictionary)
public sealed class DynamicFlowEntity<TModel> : Entity<DynamicFlowEntity<TModel>>
{
    [Index]
    public string? ReferenceId { get; set; }
    // Nested JSON snapshot using ExpandoObject (document) + primitives/arrays for provider-friendly serialization
    public ExpandoObject? Data { get; set; }
}

// Per-reference policy state persisted alongside root entity
public sealed class PolicyState<TModel> : Entity<PolicyState<TModel>>
{
    [Index]
    public string ReferenceId { get; set; } = default!;
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
    public object? StagePayload { get; set; }
    public object? Diagnostics { get; set; }
}

// Aggregation key index per model
public sealed class KeyIndex<TModel> : Entity<KeyIndex<TModel>>
{
    public string AggregationKey { get => Id; set => Id = value; }
    [Index]
    public string ReferenceId { get; set; } = default!;
}

// Reference version/projection state per model
public sealed class ReferenceItem<TModel> : Entity<ReferenceItem<TModel>>
{
    public string ReferenceId { get => Id; set => Id = value; }
    [Index]
    public ulong Version { get; set; }
    public bool RequiresProjection { get; set; }
}

// Projection task per model
public sealed class ProjectionTask<TModel> : Entity<ProjectionTask<TModel>>
{
    [Index]
    public string ReferenceId { get; set; } = default!;
    public ulong Version { get; set; }
    [Index]
    public string ViewName { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Generic view document per model
public class ProjectionView<TModel, TView> : Entity<ProjectionView<TModel, TView>>
{
    [Index]
    public string ReferenceId { get; set; } = default!;
    [Index]
    public string ViewName { get; set; } = default!;
    public TView? View { get; set; }
}

// Materialized view payload: single value per tag plus policy metadata
// Materialized payload/view types removed in greenfield; materialized snapshot is persisted as the root entity

// Strongly typed canonical and lineage views
public sealed class CanonicalProjection<TModel> : ProjectionView<TModel, Dictionary<string, string[]>> { }
public sealed class LineageProjection<TModel> : ProjectionView<TModel, Dictionary<string, Dictionary<string, string[]>>> { }
