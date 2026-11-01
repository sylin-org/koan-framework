using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Fonts;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Renders <see cref="OverlayStep"/> layers onto the host image at the
/// canonical Overlay stage (60). Per MEDIA-0004 §7:
/// <list type="bullet">
///   <item>Layers composite in declared order — lower index first (further back)</item>
///   <item>Animated host + static overlay → overlay drawn on every frame</item>
///   <item>An animated overlay contributes its first frame</item>
///   <item>Recipe nesting on overlays is depth-capped at 2</item>
///   <item>Text overlays require a registered font; missing font throws</item>
/// </list>
/// </summary>
public static class OverlayCompositor
{
    /// <summary>Max recipe-on-overlay recursion depth. See MEDIA-0004 §7.</summary>
    public const int MaxRecipeDepth = 2;

    internal static async Task ApplyAsync(
        RasterImage host,
        OverlayStep step,
        IOverlayResolver? resolver,
        KoanFontRegistry? fonts,
        MediaPipelineLimits limits,
        int currentDepth,
        ILogger logger,
        CancellationToken ct)
    {
        if (step.Layers.Length == 0) return;

        foreach (var layer in step.Layers)
        {
            ct.ThrowIfCancellationRequested();
            await DrawLayerAsync(host, layer, resolver, fonts, limits, currentDepth, logger, ct).ConfigureAwait(false);
        }
    }

    private static async Task DrawLayerAsync(
        RasterImage host,
        OverlayLayer layer,
        IOverlayResolver? resolver,
        KoanFontRegistry? fonts,
        MediaPipelineLimits limits,
        int currentDepth,
        ILogger logger,
        CancellationToken ct)
    {
        var rendered = await RenderLayerSourceAsync(layer, resolver, fonts, limits, currentDepth, logger, ct).ConfigureAwait(false);
        if (rendered is null) return; // resolver returned null; layer skipped

        // 1) scale per OverlaySize spec (against host's longest edge).
        try
        {
            rendered = Replace(rendered, ScaleLayer(rendered, host.Width, host.Height, layer.Size, limits));

            // 2) rotate (bounds expand for non-orthogonal rotations).
            var (rotatedW, rotatedH) = FrameTransforms.RotatedSize(rendered.Width, rendered.Height, layer.Rotate);
            MediaDecoder.EnsureOutputFits(limits, rotatedW, rotatedH);
            rendered = Replace(rendered, FrameTransforms.Rotate(rendered, layer.Rotate));

            // 3) resolve anchor + padding using the final rendered dimensions.
            var (drawX, drawY) = ComputeAnchor(host.Width, host.Height, rendered.Width, rendered.Height, layer.Position, layer.Padding);

            // 4) composite onto every frame of the host.
            using var image = FrameTransforms.View(rendered);
            var alpha = (byte)Math.Round(Math.Clamp(layer.Opacity, 0.0, 1.0) * byte.MaxValue);
            host.Map(host.Width, host.Height, frame =>
            {
                using var canvas = new SKCanvas(frame);
                using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
                canvas.DrawImage(image, drawX, drawY, new SKSamplingOptions(SKFilterMode.Nearest), paint);
                return frame;
            }, ct);
        }
        finally
        {
            rendered.Dispose();
        }
    }

    private static SKBitmap Replace(SKBitmap current, SKBitmap next)
    {
        if (!ReferenceEquals(current, next)) current.Dispose();
        return next;
    }

    private static async Task<SKBitmap?> RenderLayerSourceAsync(
        OverlayLayer layer,
        IOverlayResolver? resolver,
        KoanFontRegistry? fonts,
        MediaPipelineLimits limits,
        int currentDepth,
        ILogger logger,
        CancellationToken ct)
    {
        switch (layer.Source)
        {
            case MediaOverlaySource media:
                return await RenderMediaSourceAsync(media, resolver, limits, currentDepth, logger, ct).ConfigureAwait(false);
            case TextOverlaySource text:
                return RenderTextSource(text, fonts, limits);
            default:
                throw new InvalidOperationException(
                    $"Unsupported overlay source type: {layer.Source.GetType().Name}");
        }
    }

    private static async Task<SKBitmap?> RenderMediaSourceAsync(
        MediaOverlaySource source,
        IOverlayResolver? resolver,
        MediaPipelineLimits limits,
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

        // Resolver implementations apply the overlay's recipe at OpenAsync time (they have
        // IMediaRecipeRegistry access in the default Web impl); this decodes the result.
        var bytes = await MediaDecoder.ReadSourceAsync(stream, limits, ct).ConfigureAwait(false);
        using var decoded = MediaDecoder.Decode(bytes, limits, ct);
        var first = decoded.Frames[0];
        decoded.Frames.RemoveAt(0);
        return first.Bitmap;
    }

    private static SKBitmap RenderTextSource(TextOverlaySource source, KoanFontRegistry? fonts, MediaPipelineLimits limits)
    {
        if (fonts is null || !fonts.HasAny)
        {
            throw new InvalidOperationException(
                "Text overlay requested but no fonts are registered. " +
                "Call services.AddKoanFont(name, path) at startup.");
        }
        var fontName = source.Font ?? "default";
        var typeface = fonts.Find(fontName);
        if (typeface is null)
        {
            throw new InvalidOperationException(
                $"Text overlay requested unknown font '{fontName}'. " +
                $"Registered: [{string.Join(", ", fonts.Names)}].");
        }

        return TextRenderer.Render(ApplyTextTemplate(source.Text), typeface, source.FontSize, source.Color ?? BackgroundColor.White, limits);
    }

    private static string ApplyTextTemplate(string template)
    {
        // Tiny {{token}} substitution for dynamic copyright stamps etc.
        if (!template.Contains("{{", StringComparison.Ordinal)) return template;
        return template
            .Replace("{{year}}", DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private static SKBitmap ScaleLayer(SKBitmap source, int hostWidth, int hostHeight, OverlaySize size, MediaPipelineLimits limits)
    {
        switch (size.Kind)
        {
            case OverlaySizeKind.Fraction:
            {
                var longest = Math.Max(hostWidth, hostHeight);
                var targetLongest = Math.Max(1, (int)Math.Round(longest * size.FractionValue));
                var sourceLongest = Math.Max(source.Width, source.Height);
                var scale = (double)targetLongest / sourceLongest;
                var w = Math.Max(1, (int)Math.Round(source.Width * scale));
                var h = Math.Max(1, (int)Math.Round(source.Height * scale));
                MediaDecoder.EnsureOutputFits(limits, w, h);
                return FrameTransforms.Scale(source, w, h);
            }

            case OverlaySizeKind.Pixels:
            {
                var w = size.Width;
                var h = size.Height > 0
                    ? size.Height
                    : (int)Math.Round(source.Height * ((double)w / source.Width));
                MediaDecoder.EnsureOutputFits(limits, Math.Max(1, w), Math.Max(1, h));
                return FrameTransforms.Scale(source, Math.Max(1, w), Math.Max(1, h));
            }

            default:
                return source;
        }
    }

    private static (int X, int Y) ComputeAnchor(int hostWidth, int hostHeight, int layerWidth, int layerHeight, Position position, OverlayPadding padding)
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
            ? (int)Math.Round(Math.Max(hostWidth, hostHeight) * padding.FractionValue)
            : padding.Pixels;

        // Available space for the layer
        var freeX = hostWidth - layerWidth;
        var freeY = hostHeight - layerHeight;

        // X coordinate: position 0.0 = left-aligned, 1.0 = right-aligned. Padding insets toward
        // the anchored side. Center (0.5) takes no padding inset by design (it's centered).
        int x = (int)Math.Round(freeX * Math.Clamp(fx, 0.0, 1.0));
        if (fx < 0.5) x += padPx;
        else if (fx > 0.5) x -= padPx;

        int y = (int)Math.Round(freeY * Math.Clamp(fy, 0.0, 1.0));
        if (fy < 0.5) y += padPx;
        else if (fy > 0.5) y -= padPx;

        // Clamp into bounds
        x = Math.Clamp(x, 0, Math.Max(0, hostWidth - layerWidth));
        y = Math.Clamp(y, 0, Math.Max(0, hostHeight - layerHeight));
        return (x, y);
    }
}
