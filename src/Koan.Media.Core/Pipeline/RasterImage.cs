using Koan.Media.Abstractions.Recipes;
using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// The decoded working image of one render: every frame as premultiplied RGBA8888, plus what the header
/// declared. A frame's bitmap carries a color space only when the source's profile is not sRGB, which is
/// what makes an encoder embed it. Owned by one render and never shared across requests.
/// </summary>
internal sealed class RasterImage : IDisposable
{
    public RasterImage(MediaInfo info, SKEncodedOrigin origin, List<RasterFrame> frames)
    {
        Info = info;
        Origin = origin;
        Frames = frames;
    }

    /// <summary>The source as its header declared it.</summary>
    public MediaInfo Info { get; }

    /// <summary>The orientation the source declares; applied by the Orient stage.</summary>
    public SKEncodedOrigin Origin { get; }

    public List<RasterFrame> Frames { get; }

    public int Width => Frames[0].Bitmap.Width;

    public int Height => Frames[0].Bitmap.Height;

    /// <summary>Replace every frame with its transform, honouring the token between frames.</summary>
    public void Map(Func<SKBitmap, SKBitmap> transform, CancellationToken ct)
    {
        foreach (var frame in Frames)
        {
            ct.ThrowIfCancellationRequested();
            var next = transform(frame.Bitmap);
            if (ReferenceEquals(next, frame.Bitmap)) continue;
            frame.Bitmap.Dispose();
            frame.Bitmap = next;
        }
    }

    /// <summary>Drop one frame and release its pixels.</summary>
    public void RemoveFrame(int index)
    {
        Frames[index].Bitmap.Dispose();
        Frames.RemoveAt(index);
    }

    public RasterImage Clone() =>
        new(Info, Origin, Frames.Select(f => new RasterFrame(Copy(f.Bitmap), f.DurationMs)).ToList());

    public void Dispose()
    {
        foreach (var frame in Frames) frame.Bitmap.Dispose();
        Frames.Clear();
    }

    /// <summary>
    /// Allocate a transparent working bitmap. A failed allocation is a refusal of the source, never a crash.
    /// </summary>
    public static SKBitmap Allocate(int width, int height, SKColorSpace? colorSpace)
    {
        var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace)))
        {
            bitmap.Dispose();
            throw new MediaDecodeException($"Could not allocate {width}x{height} pixels for this source.");
        }
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    private static SKBitmap Copy(SKBitmap source)
    {
        var copy = Allocate(source.Width, source.Height, source.ColorSpace);
        source.GetPixelSpan().CopyTo(copy.GetPixelSpan());
        return copy;
    }
}
