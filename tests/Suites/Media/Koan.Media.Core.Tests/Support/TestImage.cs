using SkiaSharp;

namespace Koan.Media.Core.Tests.Support;

/// <summary>A color as specs write and read it: straight, not premultiplied.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Red => new(255, 0, 0);
    public static Rgba Green => new(0, 128, 0);
    public static Rgba Lime => new(0, 255, 0);
    public static Rgba SteelBlue => new(70, 130, 180);

    public SKColor ToColor() => new(R, G, B, A);
}

/// <summary>Output bytes decoded for inspection: what a client would see.</summary>
public sealed class TestImage : IDisposable
{
    private readonly SKBitmap _bitmap;

    private TestImage(SKBitmap bitmap, int frameCount, SKEncodedImageFormat format, bool hasIccProfile)
    {
        _bitmap = bitmap;
        FrameCount = frameCount;
        Format = format;
        HasIccProfile = hasIccProfile;
    }

    public int Width => _bitmap.Width;
    public int Height => _bitmap.Height;
    public int FrameCount { get; }
    public SKEncodedImageFormat Format { get; }
    public bool HasIccProfile { get; }

    /// <summary>A pixel of the first frame.</summary>
    public Rgba this[int x, int y]
    {
        get
        {
            var c = _bitmap.GetPixel(x, y);
            return new Rgba(c.Red, c.Green, c.Blue, c.Alpha);
        }
    }

    public static TestImage Load(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("Output bytes are not a readable image.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success) throw new InvalidOperationException($"Output bytes failed to decode: {result}.");
        return new TestImage(bitmap, Math.Max(1, codec.FrameCount), codec.EncodedFormat, codec.Info.ColorSpace is { IsSrgb: false });
    }

    public void Dispose() => _bitmap.Dispose();
}
