using Koan.Media.Abstractions.Recipes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Computes the target canvas for a shape+resize pass and composites
/// the shaped image onto a background derived from the recipe's
/// <see cref="Background"/>. Stage 7 (Overlay) runs before metadata
/// strip and encode; bg compose runs between the shape/resize Mutate
/// and overlay application so overlays land on the final canvas.
///
/// Returns <c>null</c> when no canvas extension is needed:
/// <list type="bullet">
///   <item>bg is transparent (current default — preserve existing behavior)</item>
///   <item>fit is not Contain (Cover/Fill leave no padding to fill)</item>
///   <item>target canvas dimensions can't be resolved (single-axis resize)</item>
///   <item>shaped image already exactly matches the target canvas</item>
/// </list>
/// Caller owns the returned canvas and is responsible for disposing it.
/// </summary>
internal static class BackgroundComposer
{
    public static Image? TryCompose(Image image, ShapeStep? shape, ResizeStep? resize, CancellationToken ct)
    {
        if (shape is null) return null;
        var bg = shape.Background;
        if (bg.Kind == BackgroundKind.Transparent) return null;
        if (shape.Fit != Fit.Contain) return null;

        var (canvasW, canvasH) = ResolveCanvasSize(shape, resize, image.Size);
        if (canvasW <= 0 || canvasH <= 0) return null;
        if (image.Width == canvasW && image.Height == canvasH) return null;

        ct.ThrowIfCancellationRequested();
        var canvas = BuildCanvas(image, canvasW, canvasH, bg, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var (x, y) = AnchorOffset(canvasW, canvasH, image.Width, image.Height, shape.Position);
            canvas.Mutate(c => c.DrawImage(image, new Point(x, y), 1f));
            return canvas;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    private static (int Width, int Height) ResolveCanvasSize(ShapeStep shape, ResizeStep? resize, Size shapedSize)
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
        // shaped image (which preserved the aspect during the Mutate pass).
        if (resize is { Width: int singleW, Height: null })
        {
            var scaled = ApplyDpr(singleW, resize.Dpr);
            var h = (int)Math.Round(shapedSize.Height * (scaled / (double)shapedSize.Width));
            return (scaled, h);
        }
        if (resize is { Width: null, Height: int singleH })
        {
            var scaled = ApplyDpr(singleH, resize.Dpr);
            var w = (int)Math.Round(shapedSize.Width * (scaled / (double)shapedSize.Height));
            return (w, scaled);
        }

        return (0, 0);
    }

    private static int ApplyDpr(int value, double dpr) =>
        dpr > 0 && Math.Abs(dpr - 1.0) > 0.001
            ? (int)Math.Round(value * dpr)
            : value;

    private static Image BuildCanvas(Image source, int width, int height, Background bg, CancellationToken ct)
    {
        return bg.Kind switch
        {
            BackgroundKind.Solid => new Image<Rgba32>(width, height, ToRgba32(bg.Color)),
            BackgroundKind.Dominant => new Image<Rgba32>(width, height, SampleDominant(source, ct)),
            BackgroundKind.Auto => new Image<Rgba32>(width, height, SampleBorderAverage(source, ct)),
            BackgroundKind.Blur => BuildBlurCanvas(source, width, height, bg.BlurRadius, ct),
            _ => new Image<Rgba32>(width, height),
        };
    }

    private static Image<Rgba32> BuildBlurCanvas(Image source, int width, int height, int radius, CancellationToken ct)
    {
        // Cover-resize a clone of the source to fill the canvas, then
        // Gaussian-blur. Radius 0 = pick a sensible default scaled to the
        // canvas's short edge (about 4%) so callers can just say bg=blur.
        var effectiveRadius = radius > 0
            ? radius
            : Math.Max(8, Math.Min(width, height) / 24);

        var canvas = source.CloneAs<Rgba32>();
        try
        {
            ct.ThrowIfCancellationRequested();
            canvas.Mutate(ctx =>
            {
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(width, height),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                });
                ctx.GaussianBlur(effectiveRadius);
            });
            return canvas;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    private static Rgba32 SampleDominant(Image source, CancellationToken ct)
    {
        // 1×1 box-resample gives the area-averaged color, a fast and
        // visually reasonable approximation of "dominant" for most
        // photographs and covers. True k-means is overkill at this scale.
        using var sample = source.CloneAs<Rgba32>();
        ct.ThrowIfCancellationRequested();
        sample.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(1, 1),
            Sampler = KnownResamplers.Box,
            Mode = ResizeMode.Stretch,
        }));
        var px = sample[0, 0];
        return new Rgba32(px.R, px.G, px.B, byte.MaxValue);
    }

    private static Rgba32 SampleBorderAverage(Image source, CancellationToken ct)
    {
        // Down-sample to a small grid so the border-strip read is cheap
        // and stable against single-pixel noise. 16×16 keeps the four
        // border strips at 16+16+14+14 = 60 samples — plenty for an
        // average that "looks like the frame" without measuring pixels
        // on full-res sources.
        using var sample = source.CloneAs<Rgba32>();
        ct.ThrowIfCancellationRequested();
        sample.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(16, 16),
            Sampler = KnownResamplers.Box,
            Mode = ResizeMode.Stretch,
        }));

        long r = 0, g = 0, b = 0;
        int n = 0;
        var w = sample.Width;
        var h = sample.Height;
        sample.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                if (y == 0 || y == h - 1)
                {
                    for (int x = 0; x < w; x++)
                    {
                        r += row[x].R; g += row[x].G; b += row[x].B; n++;
                    }
                }
                else
                {
                    var left = row[0];
                    var right = row[w - 1];
                    r += left.R + right.R;
                    g += left.G + right.G;
                    b += left.B + right.B;
                    n += 2;
                }
            }
        });

        if (n == 0) return new Rgba32(0, 0, 0, byte.MaxValue);
        return new Rgba32((byte)(r / n), (byte)(g / n), (byte)(b / n), byte.MaxValue);
    }

    private static Rgba32 ToRgba32(BackgroundColor color) =>
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
