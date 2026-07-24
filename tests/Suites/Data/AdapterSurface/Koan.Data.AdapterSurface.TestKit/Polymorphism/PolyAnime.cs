namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>DATA-0109 variant with an anime-specific payload field.</summary>
public sealed class PolyAnime : PolyMedia<PolyAnime>
{
    public int? Episodes { get; set; }
}
