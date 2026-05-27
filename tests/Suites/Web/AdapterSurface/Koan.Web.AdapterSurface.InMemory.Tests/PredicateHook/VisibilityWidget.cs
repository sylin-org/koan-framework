using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Web.AdapterSurface.InMemory.Tests.PredicateHook;

/// <summary>
/// Entity used by the WEB-0068 predicate-composition tests. Keeps its own storage row so the
/// VisibilityHook contributions don't bleed into the broader Widget surface specs.
/// </summary>
[StorageName("visibility_widgets")]
public sealed class VisibilityWidget : Entity<VisibilityWidget>
{
    public string Name { get; set; } = "";
    public VisibilityStatus Status { get; set; }
    public string? OwnerId { get; set; }
}

public enum VisibilityStatus
{
    Draft = 0,
    Published = 1,
    Hidden = 2,
}
