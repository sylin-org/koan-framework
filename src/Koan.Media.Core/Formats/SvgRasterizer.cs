using SkiaSharp;
using Svg.Skia;

namespace Koan.Media.Core.Formats;

/// <summary>
/// Svg.Skia + SkiaSharp adapter. Per MEDIA-0006 §Decision.3 — the
/// implementation of the planner's implicit Rasterize step for
/// <see cref="Koan.Media.Abstractions.Recipes.MediaKind.Vector"/>.
///
/// The output is always PNG. WebP / JPEG / AVIF re-encoding is handled by
/// the existing ImageSharp encode pass after rasterization. The aspect-
/// ratio policy is letterbox-into-transparent (no stretch, no crop).
/// </summary>
public static class SvgRasterizer
{
    /// <summary>Default PNG encode quality. Skia accepts 0..100; 100 = lossless settings.</summary>
    public const int DefaultPngQuality = 100;

    /// <summary>
    /// Render the given SVG bytes to a PNG byte array at the planner's
    /// forward-derived target dimensions. Throws
    /// <see cref="SvgRasterizationException"/> on parse or render failure.
    /// </summary>
    public static byte[] RenderToPng(byte[] svgBytes, int targetWidth, int targetHeight)
    {
        if (svgBytes is null) throw new ArgumentNullException(nameof(svgBytes));
        if (targetWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), targetWidth, "Target width must be positive.");
        if (targetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetHeight), targetHeight, "Target height must be positive.");

        using var svg = new SKSvg();
        SKPicture? picture;
        try
        {
            using var sourceStream = new MemoryStream(svgBytes, writable: false);
            picture = svg.Load(sourceStream);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.Xml.XmlException
                                   or FormatException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            throw new SvgRasterizationException(
                $"Svg.Skia failed to parse the SVG payload: {ex.Message}", ex);
        }

        if (picture is null)
        {
            throw new SvgRasterizationException(
                "Svg.Skia returned no SKPicture for the SVG payload.");
        }

        // Determine source extents: prefer the document's declared viewBox,
        // fall back to the picture cull rect (Svg.Skia honors the SVG's
        // root <svg> width/height when viewBox is absent).
        var cull = picture.CullRect;
        var srcWidth = cull.Width;
        var srcHeight = cull.Height;
        if (srcWidth <= 0 || srcHeight <= 0)
        {
            throw new SvgRasterizationException(
                $"SVG has non-positive natural extents ({srcWidth}x{srcHeight}); cannot rasterize.");
        }

        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKSurface? surface;
        try
        {
            surface = SKSurface.Create(info);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OutOfMemoryException or ArgumentException)
        {
            throw new SvgRasterizationException(
                $"Skia surface allocation failed for {targetWidth}x{targetHeight}: {ex.Message}", ex);
        }

        if (surface is null)
        {
            throw new SvgRasterizationException(
                $"SKSurface.Create returned null for {targetWidth}x{targetHeight}.");
        }

        using (surface)
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // Letterbox-into-transparent: scale uniformly to fit, center the
            // rendered viewBox in the target surface so aspect-ratio mismatch
            // produces transparent bands rather than distortion.
            var scale = Math.Min(targetWidth / srcWidth, targetHeight / srcHeight);
            var renderedW = srcWidth * scale;
            var renderedH = srcHeight * scale;
            var dx = (targetWidth - renderedW) * 0.5f;
            var dy = (targetHeight - renderedH) * 0.5f;

            canvas.Translate(dx, dy);
            canvas.Scale(scale);
            // The picture's cull rect may not start at (0,0); align it.
            canvas.Translate(-cull.Left, -cull.Top);

            try
            {
                canvas.DrawPicture(picture);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw new SvgRasterizationException(
                    $"Skia failed during SVG render: {ex.Message}", ex);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, DefaultPngQuality);
            if (data is null)
            {
                throw new SvgRasterizationException(
                    "Skia produced no PNG bytes for the rendered SVG.");
            }
            return data.ToArray();
        }
    }
}

/// <summary>
/// Thrown when the Svg.Skia / SkiaSharp rasterizer cannot produce PNG bytes
/// for an SVG payload (parse failure, surface allocation failure, render
/// failure). Always terminal at ingest — no partial blob is ever written.
/// </summary>
public sealed class SvgRasterizationException : Exception
{
    public SvgRasterizationException(string message) : base(message) { }
    public SvgRasterizationException(string message, Exception inner) : base(message, inner) { }
}
