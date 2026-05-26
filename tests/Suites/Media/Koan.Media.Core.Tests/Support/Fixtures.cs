using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Procedural test fixtures — every input is generated in-memory so
/// the suite has no on-disk asset dependency and runs identically
/// across machines and CI. Each method returns a freshly-allocated
/// stream positioned at zero; the caller takes ownership.
///
/// Pixel filling is done manually via the indexer (avoids the
/// <c>SixLabors.ImageSharp.Drawing</c> package dependency for what's
/// essentially "fill rectangle").
/// </summary>
public static class Fixtures
{
    /// <summary>1200x800 solid blue JPEG (q=85). The canonical "wide static photo" stand-in.</summary>
    public static Stream WideJpeg(int width = 1200, int height = 800, Color? fill = null)
    {
        using var img = new Image<Rgb24>(width, height);
        FillSolid(img, (fill ?? Color.SteelBlue).ToPixel<Rgb24>());
        var ms = new MemoryStream();
        img.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
        ms.Position = 0;
        return ms;
    }

    /// <summary>Square JPEG. Useful for crop / aspect tests where source is already square.</summary>
    public static Stream SquareJpeg(int side = 800, Color? fill = null) =>
        WideJpeg(side, side, fill);

    /// <summary>
    /// PNG with a transparent center quadrant. Proves alpha survives
    /// the pipeline when the encoder preserves source format.
    /// </summary>
    public static Stream TransparentPng(int width = 400, int height = 300)
    {
        using var img = new Image<Rgba32>(width, height);
        FillSolid(img, new Rgba32(255, 0, 0, 255));
        var hole = new Rectangle(width / 4, height / 4, width / 2, height / 2);
        FillRect(img, hole, new Rgba32(0, 0, 0, 0));
        var ms = new MemoryStream();
        img.SaveAsPng(ms, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Animated WebP with the requested number of frames. Each frame
    /// is a different solid color so the encoder can't accidentally
    /// collapse them into one image. The critical fixture: proves
    /// format-preservation keeps animation across the pipeline.
    /// </summary>
    public static Stream AnimatedWebp(int frames = 3, int width = 120, int height = 80)
    {
        if (frames < 2) throw new ArgumentException("Animated WebP needs >= 2 frames.", nameof(frames));
        var palette = Palette();
        using var img = new Image<Rgba32>(width, height);
        FillSolid(img, palette[0]);
        for (var i = 1; i < frames; i++)
        {
            using var next = new Image<Rgba32>(width, height);
            FillSolid(next, palette[i % palette.Length]);
            img.Frames.AddFrame(next.Frames.RootFrame);
        }

        var ms = new MemoryStream();
        img.SaveAsWebp(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        ms.Position = 0;
        return ms;
    }

    /// <summary>Animated GIF — same purpose as <see cref="AnimatedWebp"/> for GIF coverage.</summary>
    public static Stream AnimatedGif(int frames = 3, int width = 80, int height = 60)
    {
        if (frames < 2) throw new ArgumentException("Animated GIF needs >= 2 frames.", nameof(frames));
        var palette = Palette();
        using var img = new Image<Rgba32>(width, height);
        FillSolid(img, palette[0]);
        for (var i = 1; i < frames; i++)
        {
            using var next = new Image<Rgba32>(width, height);
            FillSolid(next, palette[i % palette.Length]);
            img.Frames.AddFrame(next.Frames.RootFrame);
        }

        var ms = new MemoryStream();
        img.SaveAsGif(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Bytes that don't decode as any known image format.</summary>
    public static Stream NotAnImage(int byteCount = 16)
    {
        var ms = new MemoryStream(new byte[byteCount]);
        return ms;
    }

    /// <summary>Compute SHA-256 hex digest of a stream's bytes without disposing it.</summary>
    public static async Task<string> Sha256Hex(Stream s, CancellationToken ct = default)
    {
        var pos = s.CanSeek ? s.Position : 0L;
        if (s.CanSeek) s.Position = 0;
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await Task.Run(() => sha.ComputeHash(s), ct).ConfigureAwait(false);
        if (s.CanSeek) s.Position = pos;
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Materialise the bytes of a stream without disposing the original.</summary>
    public static async Task<byte[]> Snapshot(Stream s, CancellationToken ct = default)
    {
        var pos = s.CanSeek ? s.Position : 0L;
        if (s.CanSeek) s.Position = 0;
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct).ConfigureAwait(false);
        if (s.CanSeek) s.Position = pos;
        return ms.ToArray();
    }

    private static Rgba32[] Palette() => new[]
    {
        new Rgba32(255, 0, 0, 255),     // red
        new Rgba32(0, 200, 0, 255),     // green
        new Rgba32(0, 0, 255, 255),     // blue
        new Rgba32(255, 255, 0, 255),   // yellow
        new Rgba32(255, 0, 255, 255),   // magenta
        new Rgba32(0, 255, 255, 255),   // cyan
        new Rgba32(255, 128, 0, 255),   // orange
        new Rgba32(255, 255, 255, 255), // white
    };

    private static void FillSolid<TPixel>(Image<TPixel> img, TPixel color) where TPixel : unmanaged, IPixel<TPixel>
    {
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++) row[x] = color;
            }
        });
    }

    private static void FillRect<TPixel>(Image<TPixel> img, Rectangle rect, TPixel color) where TPixel : unmanaged, IPixel<TPixel>
    {
        img.ProcessPixelRows(accessor =>
        {
            var x0 = Math.Max(0, rect.X);
            var y0 = Math.Max(0, rect.Y);
            var x1 = Math.Min(accessor.Width, rect.X + rect.Width);
            var y1 = Math.Min(accessor.Height, rect.Y + rect.Height);
            for (int y = y0; y < y1; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = x0; x < x1; x++) row[x] = color;
            }
        });
    }
}
