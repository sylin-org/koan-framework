namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Per-layer overlay configuration. One <see cref="OverlayStep"/>
/// composites N layers in declared order (lower index = drawn first /
/// further back). Per MEDIA-0004 §7.
/// </summary>
/// <param name="Source">Where the overlay bitmap / glyphs come from.</param>
/// <param name="Size">Layer size relative to host. Default: natural pixel size.</param>
/// <param name="Position">Layer anchor on the host. Default: centered.</param>
/// <param name="Padding">Inset from the anchor. Default: zero.</param>
/// <param name="Opacity">Alpha multiplier on the layer (0.0 = invisible, 1.0 = opaque).</param>
/// <param name="Rotate">Clockwise rotation in degrees applied to the layer before compositing.</param>
public sealed record OverlayLayer(
    OverlaySource Source,
    OverlaySize Size,
    Position Position,
    OverlayPadding Padding,
    double Opacity = 1.0,
    int Rotate = 0);

/// <summary>
/// Discriminated source for an overlay layer. Two variants:
/// <see cref="MediaOverlaySource"/> for compositing another media row
/// (with optional recipe), <see cref="TextOverlaySource"/> for rendering
/// short glyph strings.
/// </summary>
public abstract record OverlaySource;

/// <summary>
/// Overlay sourced from a media id. The optional <see cref="RecipeName"/>
/// pre-processes the overlay through a registered recipe (e.g. a
/// <c>mono-white</c> recipe that recolors a brand logo). Per MEDIA-0004
/// §7, recipe-on-overlay nesting is depth-capped at 2.
/// </summary>
public sealed record MediaOverlaySource(string MediaId, string? RecipeName = null) : OverlaySource;

/// <summary>
/// Overlay sourced from a text string rendered with a registered font.
/// Templates supported in <see cref="Text"/>: <c>{{year}}</c>,
/// <c>{{sourceId}}</c>, <c>{{width}}</c>, <c>{{height}}</c>.
/// </summary>
/// <param name="Text">The rendered string. May contain template tokens.</param>
/// <param name="Font">Registered font name (default: <c>default</c>).</param>
/// <param name="Color">Text color. Default: white.</param>
/// <param name="FontSize">Em-size in pixels. Default: 32.</param>
public sealed record TextOverlaySource(
    string Text,
    string? Font = null,
    BackgroundColor? Color = null,
    int FontSize = 32) : OverlaySource;
