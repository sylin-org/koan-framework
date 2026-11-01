using Koan.Media.Abstractions.Recipes;
using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Geometry on one frame. Each returns a new bitmap, or the same one when there is nothing to do; the caller
/// owns both. <see cref="SKCanvas"/> and <see cref="SKPaint"/> are local to each call because neither is thread-safe.
/// </summary>
internal static class FrameTransforms
{
    /// <summary>The size a frame has once <paramref name="origin"/> is applied.</summary>
    public static (int Width, int Height) OrientedSize(int width, int height, SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom
            ? (height, width)
            : (width, height);

    /// <summary>The size a frame has once rotated: a right angle swaps or keeps it, any other angle grows it.</summary>
    public static (int Width, int Height) RotatedSize(int width, int height, int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized % 90 == 0) return normalized is 90 or 270 ? (height, width) : (width, height);
        var radians = normalized * Math.PI / 180.0;
        var (sin, cos) = (Math.Abs(Math.Sin(radians)), Math.Abs(Math.Cos(radians)));
        return ((int)Math.Ceiling(width * cos + height * sin), (int)Math.Ceiling(width * sin + height * cos));
    }

    /// <summary>
    /// A drawable view of a bitmap's pixels with no copy of them. The bitmap must outlive it and must not change
    /// while it is in use, which holds for every source here: a canvas draws onto a different bitmap.
    /// </summary>
    public static SKImage View(SKBitmap bitmap)
    {
        using var pixels = bitmap.PeekPixels();
        return SKImage.FromPixels(pixels);
    }

    /// <summary>Apply a declared orientation, so the stored pixels become the picture as it was meant to be seen.</summary>
    public static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return source;
        var (width, height) = OrientedSize(source.Width, source.Height, origin);
        var (w, h) = ((float)source.Width, (float)source.Height);
        return Draw(source, width, height, canvas =>
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); canvas.Translate(-w, 0); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); canvas.Translate(0, -h); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom:
                    var transverse = new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1);
                    canvas.Concat(in transverse);
                    break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(-90); break;
            }
        });
    }

    /// <summary>Rotate clockwise. A right angle is exact; any other angle grows the bounds and leaves transparent corners.</summary>
    public static SKBitmap Rotate(SKBitmap source, int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized == 0) return source;
        var (w, h) = ((float)source.Width, (float)source.Height);
        var (width, height) = RotatedSize(source.Width, source.Height, normalized);

        if (normalized % 90 == 0)
        {
            return Draw(source, width, height, canvas =>
            {
                canvas.Translate(normalized == 270 ? 0 : normalized == 90 ? h : w, normalized == 90 ? 0 : normalized == 270 ? w : h);
                canvas.RotateDegrees(normalized);
            });
        }

        return Draw(source, width, height, canvas =>
        {
            canvas.Translate(width / 2f, height / 2f);
            canvas.RotateDegrees(normalized);
            canvas.Translate(-w / 2f, -h / 2f);
        }, new SKSamplingOptions(SKCubicResampler.CatmullRom));
    }

    public static SKBitmap Flip(SKBitmap source, FlipAxis axis)
    {
        var (w, h) = ((float)source.Width, (float)source.Height);
        return Draw(source, source.Width, source.Height, canvas =>
        {
            if (axis == FlipAxis.Horizontal)
            {
                canvas.Scale(-1, 1);
                canvas.Translate(-w, 0);
            }
            else
            {
                canvas.Scale(1, -1);
                canvas.Translate(0, -h);
            }
        });
    }

    public static SKBitmap Crop(SKBitmap source, SKRectI area)
    {
        if (area.Left == 0 && area.Top == 0 && area.Width == source.Width && area.Height == source.Height) return source;
        var target = RasterImage.Allocate(area.Width, area.Height, source.ColorSpace);
        ReadOnlySpan<byte> pixels = source.GetPixelSpan();
        var output = target.GetPixelSpan();
        var rowLength = area.Width * source.BytesPerPixel;
        for (var y = 0; y < area.Height; y++)
        {
            pixels.Slice((area.Top + y) * source.RowBytes + area.Left * source.BytesPerPixel, rowLength)
                .CopyTo(output.Slice(y * target.RowBytes, rowLength));
        }
        return target;
    }

    /// <summary>Scale to exactly <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static SKBitmap Scale(SKBitmap source, int width, int height) =>
        source.Width == width && source.Height == height ? source : Resampler.Resize(source, width, height);

    /// <summary>The box of the picture scaled to <paramref name="scaledWidth"/> by <paramref name="scaledHeight"/>; only the box is computed.</summary>
    public static SKBitmap ScaleToBox(SKBitmap source, int scaledWidth, int scaledHeight, SKRectI box) =>
        source.Width == scaledWidth && source.Height == scaledHeight
            ? Crop(source, box)
            : Resampler.Resize(source, scaledWidth, scaledHeight, box);

    /// <summary>Convert to sRGB and drop the profile, so the picture looks the same without it.</summary>
    public static SKBitmap DropColorProfile(SKBitmap source)
    {
        if (source.ColorSpace is null) return source;
        var target = RasterImage.Allocate(source.Width, source.Height, colorSpace: null);
        using var srgb = SKColorSpace.CreateSrgb();
        using var converted = new SKPixmap(target.Info.WithColorSpace(srgb), target.GetPixels(), target.RowBytes);
        using var pixels = source.PeekPixels();
        if (!pixels.ReadPixels(converted))
        {
            target.Dispose();
            throw new MediaDecodeException("Could not convert the source's colors to sRGB.");
        }
        return target;
    }

    private static SKBitmap Draw(SKBitmap source, int width, int height, Action<SKCanvas> place, SKSamplingOptions? sampling = null)
    {
        var target = RasterImage.Allocate(width, height, source.ColorSpace);
        using var canvas = new SKCanvas(target);
        place(canvas);
        using var image = View(source);
        canvas.DrawImage(image, 0, 0, sampling ?? new SKSamplingOptions(SKFilterMode.Nearest));
        return target;
    }
}
