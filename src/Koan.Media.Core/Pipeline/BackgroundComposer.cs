using Koan.Media.Abstractions.Recipes;
using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Computes the target canvas for a shape+resize pass and composites
/// every frame onto a background derived from the recipe's
/// <see cref="Background"/>. Stage 7 (Overlay) runs before metadata
/// strip and encode; bg compose runs between the shape/resize pass
/// and overlay application so overlays land on the final canvas.
///
/// Leaves the image as it is when no canvas extension is needed:
/// <list type="bullet">
///   <item>bg is transparent (the default)</item>
///   <item>fit is not Contain (Cover/Fill leave no padding to fill)</item>
///   <item>target canvas dimensions can't be resolved (single-axis resize)</item>
///   <item>shaped image already exactly matches the target canvas</item>
/// </list>
/// </summary>
internal static class BackgroundComposer
{
    public static void Apply(RasterImage image, ShapeStep? shape, ResizeStep? resize, CancellationToken ct)
    {
        if (shape is null) return;
        var bg = shape.Background;
        if (bg.Kind == BackgroundKind.Transparent) return;
        if (shape.Fit != Fit.Contain) return;

        var (canvasW, canvasH) = ResolveCanvasSize(shape, resize, image.Width, image.Height);
        if (canvasW <= 0 || canvasH <= 0) return;
        if (image.Width == canvasW && image.Height == canvasH) return;

        // One sampled color for the whole animation, so the fill does not flicker between frames.
        var fill = bg.Kind switch
        {
            BackgroundKind.Solid => ToColor(bg.Color),
            BackgroundKind.Dominant => SampleDominant(image.Frames[0].Bitmap),
            BackgroundKind.Auto => SampleBorderAverage(image.Frames[0].Bitmap),
            _ => SKColors.Transparent,
        };
        var (x, y) = AnchorOffset(canvasW, canvasH, image.Width, image.Height, shape.Position);

        image.Map(frame =>
        {
            var canvas = bg.Kind == BackgroundKind.Blur
                ? BuildBlurCanvas(frame, canvasW, canvasH, bg.BlurRadius)
                : RasterImage.Allocate(canvasW, canvasH, frame.ColorSpace);
            using var draw = new SKCanvas(canvas);
            if (bg.Kind != BackgroundKind.Blur) draw.Clear(fill);
            using var foreground = SKImage.FromBitmap(frame);
            draw.DrawImage(foreground, x, y, new SKSamplingOptions(SKFilterMode.Nearest));
            return canvas;
        }, ct);
    }

    private static (int Width, int Height) ResolveCanvasSize(ShapeStep shape, ResizeStep? resize, int shapedWidth, int shapedHeight)
    {
        // Resize step with both dimensions wins (target box is explicit).
        if (resize is { Width: int rw, Height: int rh })
        {
            return (ApplyDpr(rw, resize.Dpr), ApplyDpr(rh, resize.Dpr));
        }

        // Crop step with explicit pixel dimensions defines the box.
        if (shape.Crop is { Kind: CropSpecKind.Pixels or CropSpecKind.PixelsWithOffset } crop)
        {
            if (resize is { Width: int rwOnly, Height: null })
            {
                var scale = (double)ApplyDpr(rwOnly, resize.Dpr) / crop.Width;
                return (ApplyDpr(rwOnly, resize.Dpr), (int)Math.Round(crop.Height * scale));
            }
            if (resize is { Width: null, Height: int rhOnly })
            {
                var scale = (double)ApplyDpr(rhOnly, resize.Dpr) / crop.Height;
                return ((int)Math.Round(crop.Width * scale), ApplyDpr(rhOnly, resize.Dpr));
            }
            return (crop.Width, crop.Height);
        }

        // Aspect crop alone yields proportional dims; without an explicit
        // resize we can't pad a box that was never sized.
        if (shape.Crop is { Kind: CropSpecKind.Aspect } && resize is null)
        {
            return (0, 0);
        }

        // Single-axis resize with aspect crop: derive missing axis from the
        // shaped image (which preserved the aspect during the shape pass).
        if (resize is { Width: int singleW, Height: null })
        {
            var scaled = ApplyDpr(singleW, resize.Dpr);
            var h = (int)Math.Round(shapedHeight * (scaled / (double)shapedWidth));
            return (scaled, h);
        }
        if (resize is { Width: null, Height: int singleH })
        {
            var scaled = ApplyDpr(singleH, resize.Dpr);
            var w = (int)Math.Round(shapedWidth * (scaled / (double)shapedHeight));
            return (w, scaled);
        }

        return (0, 0);
    }

    private static int ApplyDpr(int value, double dpr) =>
        dpr > 0 && Math.Abs(dpr - 1.0) > 0.001
            ? (int)Math.Round(value * dpr)
            : value;

    private static SKBitmap BuildBlurCanvas(SKBitmap source, int width, int height, int radius)
    {
        // Cover-resize the frame to fill the canvas, then Gaussian-blur.
        // Radius 0 = pick a sensible default scaled to the canvas's short
        // edge (about 4%) so callers can just say bg=blur.
        var effectiveRadius = radius > 0
            ? radius
            : Math.Max(8, Math.Min(width, height) / 24);

        var (scaledW, scaledH, offsetX, offsetY) = ResizePlan.Cover(source.Width, source.Height, width, height);
        var scaled = FrameTransforms.Scale(source, scaledW, scaledH);

        var canvas = RasterImage.Allocate(width, height, source.ColorSpace);
        using var draw = new SKCanvas(canvas);
        using var blur = SKImageFilter.CreateBlur(effectiveRadius, effectiveRadius, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = blur };
        using var image = SKImage.FromBitmap(scaled);
        draw.DrawImage(image, -offsetX, -offsetY, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        if (!ReferenceEquals(scaled, source)) scaled.Dispose();
        return canvas;
    }

    private static SKColor SampleDominant(SKBitmap source)
    {
        // The area-averaged color: a fast and visually reasonable
        // approximation of "dominant" for most photographs and covers.
        // True k-means is overkill at this scale.
        var (r, g, b, a) = Average(source, new SKRectI(0, 0, source.Width, source.Height));
        return Unpremultiply(r, g, b, a);
    }

    private static SKColor SampleBorderAverage(SKBitmap source)
    {
        // Average the four strips a 16×16 grid would leave along the edge,
        // so the read is stable against single-pixel noise and "looks like
        // the frame" without weighing the inside of the picture.
        var stripW = Math.Max(1, source.Width / 16);
        var stripH = Math.Max(1, source.Height / 16);
        SKRectI[] strips =
        [
            new(0, 0, source.Width, stripH),
            new(0, source.Height - stripH, source.Width, source.Height),
            new(0, stripH, stripW, source.Height - stripH),
            new(source.Width - stripW, stripH, source.Width, source.Height - stripH),
        ];

        double r = 0, g = 0, b = 0, a = 0, weight = 0;
        foreach (var strip in strips)
        {
            if (strip.Width <= 0 || strip.Height <= 0) continue;
            var area = strip.Width * (double)strip.Height;
            var (sr, sg, sb, sa) = Average(source, strip);
            r += sr * area; g += sg * area; b += sb * area; a += sa * area;
            weight += area;
        }
        return weight == 0 ? SKColors.Black : Unpremultiply(r / weight, g / weight, b / weight, a / weight);
    }

    private static (double R, double G, double B, double A) Average(SKBitmap source, SKRectI area)
    {
        ReadOnlySpan<byte> pixels = source.GetPixelSpan();
        long r = 0, g = 0, b = 0, a = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            var row = pixels.Slice(y * source.RowBytes);
            for (var x = area.Left; x < area.Right; x++)
            {
                var at = x * 4;
                r += row[at]; g += row[at + 1]; b += row[at + 2]; a += row[at + 3];
            }
        }
        double count = area.Width * (double)area.Height;
        return (r / count, g / count, b / count, a / count);
    }

    /// <summary>An opaque color from a premultiplied average.</summary>
    private static SKColor Unpremultiply(double r, double g, double b, double a)
    {
        if (a <= 0) return SKColors.Black;
        var scale = 255.0 / a;
        return new SKColor(
            (byte)Math.Clamp(Math.Round(r * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(g * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(b * scale), 0, 255),
            byte.MaxValue);
    }

    private static SKColor ToColor(BackgroundColor color) =>
        new(color.R, color.G, color.B, color.A);

    private static (int X, int Y) AnchorOffset(int canvasW, int canvasH, int imageW, int imageH, Position position)
    {
        var freeX = Math.Max(0, canvasW - imageW);
        var freeY = Math.Max(0, canvasH - imageH);

        var xFrac = position.UseFocus ? 0.5 : position.X;
        var yFrac = position.UseFocus ? 0.5 : position.Y;

        if (position.Anchor is { } anchor)
        {
            (xFrac, yFrac) = anchor switch
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

        var x = (int)Math.Round(freeX * Math.Clamp(xFrac, 0.0, 1.0));
        var y = (int)Math.Round(freeY * Math.Clamp(yFrac, 0.0, 1.0));
        return (x, y);
    }
}
