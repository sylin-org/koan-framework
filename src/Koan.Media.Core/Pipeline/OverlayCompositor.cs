using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Fonts;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Renders <see cref="OverlayStep"/> layers onto the host image at the
/// canonical Overlay stage (60). Per MEDIA-0004 §7:
/// <list type="bullet">
///   <item>Layers composite in declared order — lower index first (further back)</item>
///   <item>Animated host + static overlay → overlay drawn on every frame</item>
///   <item>Recipe nesting on overlays is depth-capped at 2</item>
///   <item>Text overlays require a registered font; missing font throws</item>
/// </list>
/// </summary>
public static class OverlayCompositor
{
    /// <summary>Max recipe-on-overlay recursion depth. See MEDIA-0004 §7.</summary>
    public const int MaxRecipeDepth = 2;

    public static async Task ApplyAsync(
        Image hostImage,
        OverlayStep step,
        IOverlayResolver? resolver,
        KoanFontRegistry? fonts,
        int currentDepth,
        ILogger logger,
        CancellationToken ct)
    {
        if (step.Layers.Length == 0) return;

        foreach (var layer in step.Layers)
        {
            ct.ThrowIfCancellationRequested();
            await DrawLayerAsync(hostImage, layer, resolver, fonts, currentDepth, logger, ct).ConfigureAwait(false);
        }
    }

    private static async Task DrawLayerAsync(
        Image host,
        OverlayLayer layer,
        IOverlayResolver? resolver,
        KoanFontRegistry? fonts,
        int currentDepth,
        ILogger logger,
        CancellationToken ct)
    {
        using var rendered = await RenderLayerSourceAsync(layer, resolver, fonts, currentDepth, logger, ct).ConfigureAwait(false);
        if (rendered is null) return; // resolver returned null; layer skipped

        // 1) scale per OverlaySize spec (against host's longest edge). Mutate in place
        //    on the layer image we own.
        ResizeLayerInPlace(rendered, host.Size, layer.Size);

        // 2) rotate in place (ImageSharp expands bounds automatically for non-orthogonal rotations).
        if (layer.Rotate != 0)
        {
            rendered.Mutate(ctx => ctx.Rotate(layer.Rotate));
        }

        // 3) resolve anchor + padding using the final rendered dimensions.
        var (drawX, drawY) = ComputeAnchor(host.Size, rendered.Size, layer.Position, layer.Padding);

        // 4) composite — applies to every frame on animated hosts because Mutate
        //    fans across frames in ImageSharp's Image (non-generic) surface.
        var clamped = (float)Math.Clamp(layer.Opacity, 0.0, 1.0);
        host.Mutate(ctx => ctx.DrawImage(rendered, new Point(drawX, drawY), clamped));
    }

    private static async Task<Image?> RenderLayerSourceAsync(
        OverlayLayer layer,
        IOverlayResolver? resolver,
        KoanFontRegistry? fonts,
        int currentDepth,
        ILogger logger,
        CancellationToken ct)
    {
        switch (layer.Source)
        {
            case MediaOverlaySource media:
                return await RenderMediaSourceAsync(media, resolver, currentDepth, logger, ct).ConfigureAwait(false);
            case TextOverlaySource text:
                return RenderTextSource(text, fonts);
            default:
                throw new InvalidOperationException(
                    $"Unsupported overlay source type: {layer.Source.GetType().Name}");
        }
    }

    private static async Task<Image?> RenderMediaSourceAsync(
        MediaOverlaySource source,
        IOverlayResolver? resolver,
        int currentDepth,
        ILogger logger,
        CancellationToken ct)
    {
        if (resolver is null)
            throw new InvalidOperationException(
                "Overlay step requires IOverlayResolver but none was provided. " +
                "Pass one to Stream.AsMedia() or register one in DI.");

        await using var stream = await resolver.OpenAsync(source, currentDepth + 1, ct).ConfigureAwait(false);
        if (stream is null)
        {
            logger.LogWarning(
                "Overlay source '{MediaId}' could not be resolved; layer skipped.",
                source.MediaId);
            return null;
        }

        // Apply the overlay's recipe if specified — but only when we're under
        // the depth cap. Beyond the cap, draw the raw bytes.
        if (!string.IsNullOrEmpty(source.RecipeName) && currentDepth < MaxRecipeDepth)
        {
            // Resolver implementations are expected to handle recipe application
            // at OpenAsync time (they have IMediaRecipeRegistry access in the
            // default Web impl). This branch is reserved for future caller-
            // supplied recipes that should be applied here.
            // For now, just decode the (already-recipe-applied) stream.
        }

        return await Image.LoadAsync(stream, ct).ConfigureAwait(false);
    }

    private static Image RenderTextSource(TextOverlaySource source, KoanFontRegistry? fonts)
    {
        if (fonts is null || !fonts.HasAny)
        {
            throw new InvalidOperationException(
                "Text overlay requested but no fonts are registered. " +
                "Call services.AddKoanFont(name, path) at startup.");
        }
        var fontName = source.Font ?? "default";
        var font = fonts.CreateFont(fontName, source.FontSize);
        if (font is null)
        {
            throw new InvalidOperationException(
                $"Text overlay requested unknown font '{fontName}'. " +
                $"Registered: [{string.Join(", ", fonts.Names)}].");
        }

        var color = source.Color?.ToImageSharpColor() ?? Color.White;
        var rendered = ApplyTextTemplate(source.Text);

        // Measure text to size the bitmap
        var textOptions = new RichTextOptions(font);
        var bounds = TextMeasurer.MeasureBounds(rendered, textOptions);
        var width = (int)Math.Ceiling(bounds.Width) + 4; // small padding
        var height = (int)Math.Ceiling(bounds.Height) + 4;

        var img = new Image<Rgba32>(width, height);
        img.Mutate(ctx => ctx.DrawText(
            new RichTextOptions(font) { Origin = new PointF(2, 2) },
            rendered,
            color));
        return img;
    }

    private static string ApplyTextTemplate(string template)
    {
        // Tiny {{token}} substitution for dynamic copyright stamps etc.
        if (!template.Contains("{{", StringComparison.Ordinal)) return template;
        return template
            .Replace("{{year}}", DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private static void ResizeLayerInPlace(Image source, Size host, OverlaySize size)
    {
        switch (size.Kind)
        {
            case OverlaySizeKind.Natural:
                return;

            case OverlaySizeKind.Fraction:
            {
                var longest = Math.Max(host.Width, host.Height);
                var targetLongest = Math.Max(1, (int)Math.Round(longest * size.FractionValue));
                var sourceLongest = Math.Max(source.Width, source.Height);
                var scale = (double)targetLongest / sourceLongest;
                var w = Math.Max(1, (int)Math.Round(source.Width * scale));
                var h = Math.Max(1, (int)Math.Round(source.Height * scale));
                source.Mutate(ctx => ctx.Resize(w, h));
                return;
            }

            case OverlaySizeKind.Pixels:
            {
                var w = size.Width;
                var h = size.Height > 0
                    ? size.Height
                    : (int)Math.Round(source.Height * ((double)w / source.Width));
                source.Mutate(ctx => ctx.Resize(Math.Max(1, w), Math.Max(1, h)));
                return;
            }
        }
    }

    private static (int X, int Y) ComputeAnchor(Size host, Size layer, Position position, OverlayPadding padding)
    {
        // Anchor fraction in [0,1] per axis. Position.Anchor wins; otherwise X/Y.
        double fx = 0.5, fy = 0.5;
        if (position.UseFocus)
        {
            fx = 0.5; fy = 0.5;
        }
        else if (position.Anchor is { } anchor)
        {
            (fx, fy) = anchor switch
            {
                PositionAnchor.Center => (0.5, 0.5),
                PositionAnchor.Top => (0.5, 0.0),
                PositionAnchor.Bottom => (0.5, 1.0),
                PositionAnchor.Left => (0.0, 0.5),
                PositionAnchor.Right => (1.0, 0.5),
                PositionAnchor.TopLeft => (0.0, 0.0),
                PositionAnchor.TopRight => (1.0, 0.0),
                PositionAnchor.BottomLeft => (0.0, 1.0),
                PositionAnchor.BottomRight => (1.0, 1.0),
                _ => (0.5, 0.5),
            };
        }
        else
        {
            fx = position.X;
            fy = position.Y;
        }

        // Resolve padding to a pixel inset
        var padPx = padding.IsFraction
            ? (int)Math.Round(Math.Max(host.Width, host.Height) * padding.FractionValue)
            : padding.Pixels;

        // Available space for the layer
        var freeX = host.Width - layer.Width;
        var freeY = host.Height - layer.Height;

        // X coordinate: position 0.0 = left-aligned, 1.0 = right-aligned. Padding insets toward
        // the anchored side. Center (0.5) takes no padding inset by design (it's centered).
        int x = (int)Math.Round(freeX * Math.Clamp(fx, 0.0, 1.0));
        if (fx < 0.5) x += padPx;
        else if (fx > 0.5) x -= padPx;

        int y = (int)Math.Round(freeY * Math.Clamp(fy, 0.0, 1.0));
        if (fy < 0.5) y += padPx;
        else if (fy > 0.5) y -= padPx;

        // Clamp into bounds
        x = Math.Clamp(x, 0, Math.Max(0, host.Width - layer.Width));
        y = Math.Clamp(y, 0, Math.Max(0, host.Height - layer.Height));
        return (x, y);
    }
}

internal static class BackgroundColorImageSharpExtensions
{
    public static Color ToImageSharpColor(this BackgroundColor c) =>
        Color.FromRgba(c.R, c.G, c.B, c.A);
}
