using System.Collections.Concurrent;
using SkiaSharp;

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
/// bounded against arbitrary filesystem reads, and nothing depends on the
/// host having system fonts: a container image usually has none.</para>
/// </summary>
public sealed class KoanFontRegistry
{
    private readonly ConcurrentDictionary<string, SKTypeface> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names of every registered font (sorted, case-insensitive).</summary>
    public IReadOnlyList<string> Names =>
        _byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True when at least one font has been registered.</summary>
    public bool HasAny => !_byName.IsEmpty;

    /// <summary>True when a font is registered under <paramref name="name"/>.</summary>
    public bool Contains(string name) => _byName.ContainsKey(name);

    /// <summary>
    /// Register a font under <paramref name="name"/>, loading the TTF
    /// or OTF file at <paramref name="path"/>. Idempotent — registering
    /// the same name twice replaces the family.
    /// </summary>
    public KoanFontRegistry Register(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Font name required.", nameof(name));
        if (!File.Exists(path)) throw new FileNotFoundException($"Font file not found: {path}");
        _byName[name] = SKTypeface.FromFile(path)
            ?? throw new InvalidDataException($"Font file could not be read: {path}");
        return this;
    }

    /// <summary>The registered typeface; shared across renders, which it is safe for.</summary>
    internal SKTypeface? Find(string name) => _byName.GetValueOrDefault(name);
}
