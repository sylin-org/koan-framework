using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>One frame of a <see cref="RasterImage"/>: its pixels and how long it shows.</summary>
internal sealed class RasterFrame
{
    public RasterFrame(SKBitmap bitmap, int durationMs)
    {
        Bitmap = bitmap;
        DurationMs = durationMs;
    }

    public SKBitmap Bitmap { get; set; }

    public int DurationMs { get; }
}
