using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Web.AdapterSurface.InMemory.Tests.RelationshipExpansion;

/// <summary>
/// AN-leak — the child type. Two distinct edges to the SAME target (<see cref="Maker"/>) via different
/// fields (<see cref="AuthorId"/>, <see cref="ReviewerId"/>) — the T2 "divergent edges, same target"
/// case. <see cref="WorkVisibilityHook"/> walls non-Published works for anonymous callers.
/// </summary>
[StorageName("an_works")]
public sealed class Work : Entity<Work>
{
    public string Title { get; set; } = "";

    public WorkStatus Status { get; set; }

    [Parent(typeof(Maker))]
    public string? AuthorId { get; set; }

    [Parent(typeof(Maker))]
    public string? ReviewerId { get; set; }
}

public enum WorkStatus
{
    Draft = 0,
    Published = 1,
}
