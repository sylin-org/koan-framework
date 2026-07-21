using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Web.AdapterSurface.InMemory.Tests.RelationshipExpansion;

/// <summary>
/// AN-leak — the parent/target type in the relationship-expansion visibility tests. A <c>Secret</c>
/// maker is hidden from anonymous callers by <see cref="MakerVisibilityHook"/>, modeling a walled
/// parent that must be omitted from an expanded graph (T-parent).
/// </summary>
[StorageName("an_makers")]
public sealed class Maker : Entity<Maker>
{
    public string Name { get; set; } = "";

    public bool Secret { get; set; }
}
