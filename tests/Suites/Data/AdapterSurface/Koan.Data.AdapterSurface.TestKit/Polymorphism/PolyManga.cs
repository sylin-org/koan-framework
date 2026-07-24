namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>DATA-0109 variant with manga-specific payload fields.</summary>
public sealed class PolyManga : PolyMedia<PolyManga>
{
    public int? Volumes { get; set; }
    public int? Chapters { get; set; }
}
