using System.Globalization;
using System.Text;

namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Bitset of <see cref="MediaKind"/>. Encodes encoder admission sets
/// (e.g. <c>jpeg.Accepts = { Raster }</c>) and step input acceptance
/// sets (e.g. <c>SampleStep.AcceptsFrom = { Raster, AnimatedRaster,
/// Vector, Timeline }</c>). Immutable value type. Per MEDIA-0005 §3.
/// </summary>
public readonly struct KindSet : IEquatable<KindSet>
{
    private readonly int _mask;

    private KindSet(int mask) => _mask = mask;

    /// <summary>The empty set.</summary>
    public static KindSet None => new(0);

    /// <summary>All four kinds — the default for steps that don't constrain their input.</summary>
    public static KindSet All => new(BitFor(MediaKind.Raster)
                                     | BitFor(MediaKind.AnimatedRaster)
                                     | BitFor(MediaKind.Vector)
                                     | BitFor(MediaKind.Timeline));

    /// <summary>Construct a set from the listed kinds.</summary>
    public static KindSet Of(params MediaKind[] kinds)
    {
        if (kinds is null || kinds.Length == 0) return None;
        var mask = 0;
        foreach (var k in kinds) mask |= BitFor(k);
        return new KindSet(mask);
    }

    /// <summary>True when this set contains the given kind.</summary>
    public bool Contains(MediaKind k) => (_mask & BitFor(k)) != 0;

    /// <summary>Union with another set (this | other).</summary>
    public KindSet Union(KindSet other) => new(_mask | other._mask);

    /// <summary>Intersection with another set (this &amp; other).</summary>
    public KindSet Intersect(KindSet other) => new(_mask & other._mask);

    /// <summary>True when this set has no kinds.</summary>
    public bool IsEmpty => _mask == 0;

    /// <summary>Number of kinds in this set.</summary>
    public int Count => System.Numerics.BitOperations.PopCount((uint)_mask);

    /// <summary>Enumerate the kinds in declaration order (Raster, AnimatedRaster, Vector, Timeline).</summary>
    public IEnumerable<MediaKind> Enumerate()
    {
        foreach (MediaKind k in Enum.GetValues<MediaKind>())
        {
            if (Contains(k)) yield return k;
        }
    }

    public static KindSet operator |(KindSet a, KindSet b) => a.Union(b);
    public static KindSet operator &(KindSet a, KindSet b) => a.Intersect(b);

    public bool Equals(KindSet other) => _mask == other._mask;
    public override bool Equals(object? obj) => obj is KindSet other && Equals(other);
    public override int GetHashCode() => _mask;
    public static bool operator ==(KindSet a, KindSet b) => a._mask == b._mask;
    public static bool operator !=(KindSet a, KindSet b) => a._mask != b._mask;

    public override string ToString()
    {
        if (IsEmpty) return "{}";
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var k in Enumerate())
        {
            if (!first) sb.Append(", ");
            sb.Append(k.ToString());
            first = false;
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static int BitFor(MediaKind k) => 1 << (int)k;
}
