using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;

namespace Sora.Flow.Model;

public sealed class ProjectionTask : Entity<ProjectionTask>
{
    [Index]
    public string ReferenceId { get; set; } = default!;
    public ulong Version { get; set; }
    [Index]
    public string ViewName { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectionView<TView> : Entity<ProjectionView<TView>>
{
    [Index]
    public string ReferenceId { get; set; } = default!;
    [Index]
    public string ViewName { get; set; } = default!;
    public TView? View { get; set; }

    // Convenience for per-view set
    public static class Set
    {
        public static string Of(string viewName) => viewName;
    }
}
