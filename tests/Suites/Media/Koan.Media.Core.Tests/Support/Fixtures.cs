using System.Text;
using SkiaSharp;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Procedural test fixtures — every input is generated in-memory so
/// the suite has no on-disk asset dependency and runs identically
/// across machines and CI. Each method returns a freshly-allocated
/// stream positioned at zero; the caller takes ownership. Formats the
/// engine cannot write (GIF, BMP, ICO, WBMP, TIFF) and the metadata it
/// never writes are assembled here byte by byte.
/// </summary>
public static class Fixtures
{
    /// <summary>1200x800 solid blue JPEG (q=85). The canonical "wide static photo" stand-in.</summary>
    public static Stream WideJpeg(int width = 1200, int height = 800, Rgba? fill = null)
    {
        using var bitmap = Solid(width, height, fill ?? Rgba.SteelBlue);
        return Encode(bitmap, SKEncodedImageFormat.Jpeg, 85);
    }

    /// <summary>Square JPEG. Useful for crop / aspect tests where source is already square.</summary>
    public static Stream SquareJpeg(int side = 800, Rgba? fill = null) =>
        WideJpeg(side, side, fill);

    /// <summary>A PNG of one solid color.</summary>
    public static MemoryStream SolidPng(int width, int height, Rgba color)
    {
        using var bitmap = Solid(width, height, color);
        return Encode(bitmap, SKEncodedImageFormat.Png, 100);
    }

    /// <summary>
    /// PNG with a transparent center quadrant. Proves alpha survives
    /// the pipeline when the encoder preserves source format.
    /// </summary>
    public static Stream TransparentPng(int width = 400, int height = 300)
    {
        using var bitmap = Solid(width, height, new Rgba(255, 0, 0));
        bitmap.Erase(SKColors.Transparent, SKRectI.Create(width / 4, height / 4, width / 2, height / 2));
        return Encode(bitmap, SKEncodedImageFormat.Png, 100);
    }

    /// <summary>
    /// Animated WebP with the requested number of frames, 100 ms each. Each
    /// frame is a different solid color so the encoder can't accidentally
    /// collapse them into one image. The critical fixture: proves
    /// format-preservation keeps animation across the pipeline.
    /// </summary>
    public static Stream AnimatedWebp(int frames = 3, int width = 120, int height = 80)
    {
        if (frames < 2) throw new ArgumentException("Animated WebP needs >= 2 frames.", nameof(frames));
        var bitmaps = Enumerable.Range(0, frames).Select(i => Solid(width, height, Palette[i % Palette.Length])).ToArray();
        try
        {
            var encoded = bitmaps.Select(b => new SKWebpEncoderFrame(b, TimeSpan.FromMilliseconds(100))).ToArray();
            using var data = SKWebpEncoder.EncodeAnimated(encoded, new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100))!;
            return new MemoryStream(data.ToArray());
        }
        finally
        {
            foreach (var b in bitmaps) b.Dispose();
        }
    }

    /// <summary>
    /// Animated GIF, one solid color per frame. Written with a code size that never
    /// grows: a clear code before every pixel keeps LZW from compressing anything.
    /// </summary>
    public static Stream AnimatedGif(int frames = 3, int width = 80, int height = 60)
    {
        if (frames < 2) throw new ArgumentException("Animated GIF needs >= 2 frames.", nameof(frames));
        var gif = new MemoryStream();
        gif.Write(Encoding.ASCII.GetBytes("GIF89a"));
        WriteUInt16(gif, width);
        WriteUInt16(gif, height);
        gif.WriteByte(0xF2); // global table of 8 entries
        gif.WriteByte(0);
        gif.WriteByte(0);
        for (var i = 0; i < 8; i++)
        {
            var c = Palette[i];
            gif.WriteByte(c.R); gif.WriteByte(c.G); gif.WriteByte(c.B);
        }
        gif.Write([0x21, 0xFF, 0x0B]);
        gif.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        gif.Write([0x03, 0x01, 0x00, 0x00, 0x00]);
        for (var f = 0; f < frames; f++)
        {
            gif.Write([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]); // 100 ms
            gif.WriteByte(0x2C);
            WriteUInt16(gif, 0);
            WriteUInt16(gif, 0);
            WriteUInt16(gif, width);
            WriteUInt16(gif, height);
            gif.WriteByte(0);
            gif.WriteByte(3); // minimum code size: 3, so codes are 4 bits wide
            WriteGifPixels(gif, width * height, (byte)(f % 8));
            gif.WriteByte(0);
        }
        gif.WriteByte(0x3B);
        gif.Position = 0;
        return gif;
    }

    /// <summary>An uncompressed 24-bit BMP of one color.</summary>
    public static Stream Bmp(int width, int height, Rgba color)
    {
        var rowBytes = (width * 3 + 3) / 4 * 4;
        var bmp = new MemoryStream();
        bmp.Write("BM"u8);
        WriteUInt32(bmp, 54 + rowBytes * height);
        WriteUInt32(bmp, 0);
        WriteUInt32(bmp, 54);
        WriteUInt32(bmp, 40);
        WriteUInt32(bmp, width);
        WriteUInt32(bmp, height);
        WriteUInt16(bmp, 1);
        WriteUInt16(bmp, 24);
        for (var i = 0; i < 6; i++) WriteUInt32(bmp, 0);
        var row = new byte[rowBytes];
        for (var x = 0; x < width; x++)
        {
            row[x * 3] = color.B; row[x * 3 + 1] = color.G; row[x * 3 + 2] = color.R;
        }
        for (var y = 0; y < height; y++) bmp.Write(row);
        bmp.Position = 0;
        return bmp;
    }

    /// <summary>An ICO holding one PNG image.</summary>
    public static Stream Ico(int side, Rgba color)
    {
        var png = SolidPng(side, side, color).ToArray();
        var ico = new MemoryStream();
        WriteUInt16(ico, 0);
        WriteUInt16(ico, 1);
        WriteUInt16(ico, 1);
        ico.WriteByte((byte)side);
        ico.WriteByte((byte)side);
        ico.WriteByte(0);
        ico.WriteByte(0);
        WriteUInt16(ico, 1);
        WriteUInt16(ico, 32);
        WriteUInt32(ico, png.Length);
        WriteUInt32(ico, 22);
        ico.Write(png);
        ico.Position = 0;
        return ico;
    }

    /// <summary>A WBMP, all white.</summary>
    public static Stream Wbmp(byte width, byte height)
    {
        var rowBytes = (width + 7) / 8;
        var wbmp = new MemoryStream();
        wbmp.Write([0, 0, width, height]);
        wbmp.Write(Enumerable.Repeat((byte)0xFF, rowBytes * height).ToArray());
        wbmp.Position = 0;
        return wbmp;
    }

    /// <summary>A valid little TIFF: one white pixel.</summary>
    public static Stream Tiff()
    {
        var tiff = new MemoryStream();
        tiff.Write("II*\0"u8);
        WriteUInt32(tiff, 8);
        WriteUInt16(tiff, 6);
        foreach (var (tag, type, value) in new (int, int, int)[]
        {
            (256, 3, 1), (257, 3, 1), (258, 3, 8), (262, 3, 1), (273, 4, 86), (279, 4, 1),
        })
        {
            WriteUInt16(tiff, tag);
            WriteUInt16(tiff, type);
            WriteUInt32(tiff, 1);
            WriteUInt32(tiff, value);
        }
        WriteUInt32(tiff, 0);
        tiff.WriteByte(0xFF);
        tiff.Position = 0;
        return tiff;
    }

    /// <summary>A PNG in a Display P3 color space, carrying that profile.</summary>
    public static Stream DisplayP3Png(int width, int height, Rgba color)
    {
        using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, p3));
        bitmap.Erase(color.ToColor());
        // Erase converts from sRGB; these are the raw values a P3 file would store.
        var pixels = bitmap.GetPixelSpan();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.R; pixels[i + 1] = color.G; pixels[i + 2] = color.B; pixels[i + 3] = color.A;
        }
        using var encoded = bitmap.PeekPixels()!;
        using var data = encoded.Encode(SKPngEncoderOptions.Default)!;
        return new MemoryStream(data.ToArray());
    }

    /// <summary>A PNG with an extra chunk before its image data.</summary>
    public static Stream PngWithChunk(Stream png, string type, byte[] payload)
    {
        var bytes = Read(png);
        // Signature (8) + IHDR (4 length + 4 type + 13 data + 4 CRC).
        const int afterHeader = 33;
        var chunk = new MemoryStream();
        var typed = Encoding.ASCII.GetBytes(type).Concat(payload).ToArray();
        WriteBigEndian(chunk, (uint)payload.Length);
        chunk.Write(typed);
        WriteBigEndian(chunk, Crc32(typed));
        return new MemoryStream(bytes[..afterHeader].Concat(chunk.ToArray()).Concat(bytes[afterHeader..]).ToArray());
    }

    /// <summary>An animated PNG's control chunk on an otherwise ordinary PNG: it declares more frames than it reads as.</summary>
    public static Stream ApngDeclaring(int frames, Rgba color)
    {
        var actl = new MemoryStream();
        WriteBigEndian(actl, (uint)frames);
        WriteBigEndian(actl, 0);
        return PngWithChunk(SolidPng(16, 16, color), "acTL", actl.ToArray());
    }

    /// <summary>A JPEG with an extra application segment right after its start marker.</summary>
    public static Stream JpegWithSegment(Stream jpeg, byte marker, byte[] payload)
    {
        var bytes = Read(jpeg);
        var length = payload.Length + 2;
        byte[] header = [0xFF, marker, (byte)(length >> 8), (byte)length];
        return new MemoryStream(bytes[..2].Concat(header).Concat(payload).Concat(bytes[2..]).ToArray());
    }

    /// <summary>Bytes that don't decode as any known image format.</summary>
    public static Stream NotAnImage(int byteCount = 16)
    {
        var ms = new MemoryStream(new byte[byteCount]);
        return ms;
    }

    /// <summary>
    /// JPEG with the given EXIF orientation tag set. Two horizontal
    /// stripes (red on top half, blue on bottom half) so an
    /// auto-orient pass that runs has detectable visual evidence —
    /// after correct orientation, the red stripe is always on top.
    ///
    /// <para>Orientation values per the EXIF spec:</para>
    /// <list type="bullet">
    ///   <item>1 — no rotation (identity)</item>
    ///   <item>3 — 180° rotation</item>
    ///   <item>6 — 90° clockwise rotation (very common, iPhone portrait)</item>
    ///   <item>8 — 90° counter-clockwise rotation</item>
    /// </list>
    /// </summary>
    /// <param name="orientation">The tag's value.</param>
    /// <param name="width">Width of the picture as it is meant to be seen.</param>
    /// <param name="height">Height of the picture as it is meant to be seen.</param>
    /// <param name="description">Planted as the Exif image description, to look for in the output.</param>
    public static Stream JpegWithExifOrientation(ushort orientation, int width = 200, int height = 100, string? description = null)
    {
        // Build the LOGICAL (post-AutoOrient) image first: red top, blue
        // bottom at (width, height). Then rotate to the STORED bytes so
        // the orient stage applying the EXIF correction restores the logical view.
        using var logical = Solid(width, height, new Rgba(255, 0, 0));
        logical.Erase(new SKColor(0, 0, 255), SKRectI.Create(0, height / 2, width, height - height / 2));

        // For orientation=6 the viewer rotates 90° CW; the file therefore
        // stores the image 90° CCW from the logical view. Symmetric for 8.
        var degrees = orientation switch { 3 => 180, 6 => 270, 8 => 90, _ => 0 };
        var swap = degrees is 90 or 270;
        using var stored = new SKBitmap(new SKImageInfo(swap ? height : width, swap ? width : height));
        using (var canvas = new SKCanvas(stored))
        {
            canvas.Translate(stored.Width / 2f, stored.Height / 2f);
            canvas.RotateDegrees(degrees);
            canvas.Translate(-width / 2f, -height / 2f);
            using var image = SKImage.FromBitmap(logical);
            canvas.DrawImage(image, 0, 0);
        }

        using var jpeg = Encode(stored, SKEncodedImageFormat.Jpeg, 95);
        return JpegWithSegment(jpeg, 0xE1, ExifSegment(orientation, description));
    }

    /// <summary>
    /// JPEG with a solid border and a different solid inner region.
    /// Useful for Auto bg tests: the border-sample average should
    /// resolve to <paramref name="borderColor"/>, while the center of
    /// the source is <paramref name="innerColor"/>.
    /// </summary>
    public static Stream JpegWithBorder(
        Rgba innerColor,
        Rgba borderColor,
        int width = 400,
        int height = 400,
        int borderThickness = 40)
    {
        using var bitmap = Solid(width, height, borderColor);
        bitmap.Erase(innerColor.ToColor(), SKRectI.Create(
            borderThickness, borderThickness,
            Math.Max(1, width - 2 * borderThickness),
            Math.Max(1, height - 2 * borderThickness)));
        return Encode(bitmap, SKEncodedImageFormat.Jpeg, 95);
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

    private static readonly Rgba[] Palette =
    [
        new(255, 0, 0),     // red
        new(0, 200, 0),     // green
        new(0, 0, 255),     // blue
        new(255, 255, 0),   // yellow
        new(255, 0, 255),   // magenta
        new(0, 255, 255),   // cyan
        new(255, 128, 0),   // orange
        new(255, 255, 255), // white
    ];

    private static SKBitmap Solid(int width, int height, Rgba color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color.ToColor());
        return bitmap;
    }

    private static MemoryStream Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var data = bitmap.Encode(format, quality);
        return new MemoryStream(data.ToArray());
    }

    private static byte[] Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] ExifSegment(ushort orientation, string? description)
    {
        var text = description is null ? [] : Encoding.ASCII.GetBytes(description + "\0");
        var entries = description is null ? 1 : 2;
        var textOffset = 8 + 2 + entries * 12 + 4;
        var tiff = new MemoryStream();
        tiff.Write("II*\0"u8);
        WriteUInt32(tiff, 8);
        WriteUInt16(tiff, entries);
        if (description is not null)
        {
            WriteUInt16(tiff, 0x010E); // ImageDescription, ASCII
            WriteUInt16(tiff, 2);
            WriteUInt32(tiff, text.Length);
            WriteUInt32(tiff, textOffset);
        }
        WriteUInt16(tiff, 0x0112); // Orientation, SHORT
        WriteUInt16(tiff, 3);
        WriteUInt32(tiff, 1);
        WriteUInt16(tiff, orientation);
        WriteUInt16(tiff, 0);
        WriteUInt32(tiff, 0);
        tiff.Write(text);
        return "Exif\0\0"u8.ToArray().Concat(tiff.ToArray()).ToArray();
    }

    private static void WriteGifPixels(Stream gif, int pixels, byte index)
    {
        const int clear = 8;
        var codes = new MemoryStream();
        int bits = 0, count = 0;
        void Emit(int code)
        {
            bits |= code << count;
            count += 4;
            while (count >= 8)
            {
                codes.WriteByte((byte)bits);
                bits >>= 8;
                count -= 8;
            }
        }
        for (var i = 0; i < pixels; i++)
        {
            Emit(clear);
            Emit(index);
        }
        Emit(clear + 1);
        if (count > 0) codes.WriteByte((byte)bits);

        var all = codes.ToArray();
        for (var at = 0; at < all.Length; at += 255)
        {
            var length = Math.Min(255, all.Length - at);
            gif.WriteByte((byte)length);
            gif.Write(all, at, length);
        }
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private static void WriteUInt16(Stream s, int value)
    {
        s.WriteByte((byte)value);
        s.WriteByte((byte)(value >> 8));
    }

    private static void WriteUInt32(Stream s, int value)
    {
        WriteUInt16(s, value & 0xFFFF);
        WriteUInt16(s, (value >> 16) & 0xFFFF);
    }

    private static void WriteBigEndian(Stream s, uint value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }
}
