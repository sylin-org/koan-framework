using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;
using System.Collections.Generic;

namespace Sora.Flow.Model;

// Canonical model base marker; no behavior beyond Entity<T>
public abstract class FlowEntity<TModel> : Entity<TModel> where TModel : FlowEntity<TModel>, new() { }

// Normalized transport/delta for a model: Id + dynamic data (JObject or dictionary)
public sealed class DynamicFlowEntity<TModel> : Entity<DynamicFlowEntity<TModel>>
{
    [Index]
    public string? ReferenceId { get; set; }
    public object? Data { get; set; }
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

// Strongly typed canonical and lineage views
public sealed class CanonicalProjection<TModel> : ProjectionView<TModel, Dictionary<string, string[]>> { }
public sealed class LineageProjection<TModel> : ProjectionView<TModel, Dictionary<string, Dictionary<string, string[]>>> { }
