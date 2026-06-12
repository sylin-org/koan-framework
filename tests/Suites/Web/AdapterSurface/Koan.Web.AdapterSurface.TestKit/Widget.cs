using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// Entity exercising scalar + nested collection fields. Used by AdapterSurfaceSpecsBase
/// to validate the full EntityController surface against each Koan data adapter.
///
/// <para>
/// The explicit <see cref="StorageNameAttribute"/> keeps the storage name short and
/// provider-friendly — the fully-qualified type name contains dots, which Sqlite mis-quotes
/// during DDL generation. All adapters honour [StorageName] via the framework's
/// IStorageNameResolver pipeline.
/// </para>
/// </summary>
[StorageName("widgets_surface")]
public sealed class Widget : Entity<Widget>
{
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Sighting> Sightings { get; set; } = new();
}

public sealed class Sighting
{
    public string Location { get; set; } = "";
    public DateTimeOffset LastChangedAt { get; set; }
    public int Index { get; set; }
}
