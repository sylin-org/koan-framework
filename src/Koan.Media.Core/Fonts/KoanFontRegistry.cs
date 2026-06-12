using System.Collections.Concurrent;
using SixLabors.Fonts;

namespace Koan.Media.Core.Fonts;

/// <summary>
/// Registry of TTF/OTF fonts available to text overlays. Apps register
/// fonts at startup via
/// <see cref="ServiceCollectionFontExtensions.AddKoanFont"/>; the
/// overlay compositor reads from this registry when a
/// <c>TextOverlaySource</c> names a font.
///
/// <para>Per MEDIA-0004 §7, only registered fonts are usable — text
/// overlays naming an unknown font are rejected. Keeps the surface
/// bounded against arbitrary filesystem reads.</para>
/// </summary>
public sealed class KoanFontRegistry
{
    private readonly FontCollection _collection = new();
    private readonly ConcurrentDictionary<string, FontFamily> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names of every registered font (sorted, case-insensitive).</summary>
    public IReadOnlyList<string> Names =>
        _byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True when at least one font has been registered.</summary>
    public bool HasAny => !_byName.IsEmpty;

    /// <summary>
    /// Register a font under <paramref name="name"/>, loading the TTF
    /// or OTF file at <paramref name="path"/>. Idempotent — registering
    /// the same name twice replaces the family.
    /// </summary>
    public KoanFontRegistry Register(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Font name required.", nameof(name));
        if (!File.Exists(path)) throw new FileNotFoundException($"Font file not found: {path}");
        var family = _collection.Add(path);
        _byName[name] = family;
        return this;
    }

    /// <summary>
    /// Resolve a registered font name to a usable <see cref="Font"/>
    /// at the requested em-size. Returns null when the name isn't
    /// registered.
    /// </summary>
    public Font? CreateFont(string name, float emSize)
    {
        return _byName.TryGetValue(name, out var family)
            ? family.CreateFont(emSize)
            : null;
    }
}
