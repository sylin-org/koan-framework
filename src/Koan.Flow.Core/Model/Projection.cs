using Koan.Data.Core.Model;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Flow.Model;

public sealed class ProjectionTask : Entity<ProjectionTask>
{
    // Deprecated in favor of typed ProjectionTask<TModel>
    public ulong Version { get; set; }
    [Index]
    public string ViewName { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ProjectionView<TView> : Entity<ProjectionView<TView>>
{
    // Deprecated in favor of typed ProjectionView<TModel, TView>
    [Index]
    public string? CanonicalId { get; set; }
    [Index]
    public string? ReferenceId { get; set; }
    [Index]
    public string ViewName { get; set; } = default!;
    public TView? View { get; set; }

    // Convenience for per-view set
    public static class Set
    {
        public static string Of(string viewName) => viewName;
    }
}

// Strongly-typed view documents to ensure Mongo can serialize view payloads
// Canonical view: tag -> [values]
public sealed class CanonicalProjectionView : Entity<CanonicalProjectionView>
{
    // UUID-first; include CanonicalId for business lookups
    [Index]
    public string? CanonicalId { get; set; }
    [Index]
    public string? ReferenceId { get; set; }
    [Index]
    public string ViewName { get; set; } = default!;
    public object? Model { get; set; }
}

// Lineage view: tag -> value -> [sources]
public sealed class LineageProjectionView : ProjectionView<Dictionary<string, Dictionary<string, string[]>>> { }
