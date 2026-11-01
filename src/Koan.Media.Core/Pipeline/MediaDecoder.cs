using Koan.Media.Abstractions.Recipes;
using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Reads source bytes and turns them into a <see cref="RasterImage"/>. Dimensions and frame count come from
/// the header and every limit is applied before any pixel is allocated, on non-seekable input as well.
/// Anything other than a complete decode is refused with <see cref="MediaDecodeException"/>: partial images
/// are never served.
/// </summary>
internal static class MediaDecoder
{
    /// <summary>
    /// The most source bytes one pipeline buffers when the caller's limits name no lower cap. Decoding runs
    /// over the whole buffer and takes no token, so the source is read first, asynchronously and cancellably.
    /// </summary>
    public const long SourceBytesCeiling = 1L << 30;

    /// <summary>
    /// The most pixels one source may decode to across all its frames: 2 GiB of premultiplied RGBA. Applies
    /// whatever the caller's limits say, because a frame count and a frame size that each pass can still multiply
    /// into an allocation no host survives.
    /// </summary>
    public const int MaxDecodedMegapixels = 512;

    private const int ReadChunkBytes = 81_920;

    /// <summary>Buffer the whole source, refusing it as soon as it passes the byte limit.</summary>
    public static async Task<byte[]> ReadSourceAsync(Stream source, MediaPipelineLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cap = limits.MaxSourceBytes > 0 ? Math.Min(limits.MaxSourceBytes, SourceBytesCeiling) : SourceBytesCeiling;

        if (source.CanSeek)
        {
            source.Position = 0;
            if (source.Length > cap) throw new MediaSourceLimitException("maxSourceBytes", source.Length, cap);
            var exact = new byte[source.Length];
            await source.ReadExactlyAsync(exact, ct).ConfigureAwait(false);
            return exact;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        while (true)
        {
            var read = await source.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > cap) throw new MediaSourceLimitException("maxSourceBytes", buffer.Length + read, cap);
            buffer.Write(chunk, 0, read);
        }
    }

    /// <summary>What the header declares, with the limits applied. Allocates no pixels.</summary>
    public static MediaInfo Probe(byte[] source, MediaPipelineLimits limits)
    {
        using var data = OpenData(source);
        using var codec = OpenCodec(data);
        return ReadHeader(codec, limits);
    }

    /// <summary>Decode every frame, or refuse.</summary>
    public static RasterImage Decode(byte[] source, MediaPipelineLimits limits, CancellationToken ct)
    {
        using var data = OpenData(source);
        using var codec = OpenCodec(data);
        var info = ReadHeader(codec, limits);
        var colorSpace = TaggedColorSpace(codec);

        var frames = new List<RasterFrame>(info.FrameCount);
        var image = new RasterImage(info, codec.EncodedOrigin, frames);
        try
        {
            if (codec.FrameCount <= 1)
            {
                var duration = codec.FrameCount == 1 ? codec.FrameInfo[0].Duration : 0;
                frames.Add(new RasterFrame(DecodeFrame(codec, colorSpace, SKCodecOptions.Default), duration));
                return image;
            }

            var frameInfo = codec.FrameInfo;
            for (var i = 0; i < frameInfo.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                // A frame that builds on an earlier one decodes onto a copy of it, so a long animation costs
                // one pass over its frames rather than a replay from the start for each.
                var required = frameInfo[i].RequiredFrame;
                var options = required >= 0 ? new SKCodecOptions(i, required) : new SKCodecOptions(i);
                var bitmap = RasterImage.Allocate(info.Width, info.Height, colorSpace);
                if (required >= 0) frames[required].Bitmap.GetPixelSpan().CopyTo(bitmap.GetPixelSpan());
                DecodeInto(codec, bitmap, options);
                frames.Add(new RasterFrame(bitmap, frameInfo[i].Duration));
            }
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static SKData OpenData(byte[] source)
    {
        if (source.Length == 0) throw new MediaDecodeException("Source is empty.");
        return SKData.CreateCopy(source);
    }

    private static SKCodec OpenCodec(SKData data) =>
        SKCodec.Create(data)
        ?? throw new MediaDecodeException("Source bytes did not match any readable image format.");

    private static MediaInfo ReadHeader(SKCodec codec, MediaPipelineLimits limits)
    {
        var format = ReadableSlug(codec.EncodedFormat)
            ?? throw new MediaDecodeException($"Source format '{codec.EncodedFormat}' is not readable.");
        var header = codec.Info;
        var pixels = header.Width * (long)header.Height;
        var frameCount = Math.Max(1, codec.FrameCount);

        if (limits.MaxSourceMegapixels > 0)
        {
            // Round up so a 100.01-megapixel source still trips a 100-cap.
            var megapixels = (long)Math.Ceiling(pixels / 1_000_000.0);
            if (megapixels > limits.MaxSourceMegapixels)
                throw new MediaSourceLimitException("maxSourceMegapixels", megapixels, limits.MaxSourceMegapixels);
        }
        if (limits.MaxFrameCount > 0 && frameCount > limits.MaxFrameCount)
            throw new MediaSourceLimitException("maxFrameCount", frameCount, limits.MaxFrameCount);

        var decoded = (long)Math.Ceiling(pixels * (double)frameCount / 1_000_000.0);
        if (decoded > MaxDecodedMegapixels)
            throw new MediaSourceLimitException("maxDecodedMegapixels", decoded, MaxDecodedMegapixels);

        var origin = codec.EncodedOrigin;
        return new MediaInfo(
            Format: format,
            Width: header.Width,
            Height: header.Height,
            FrameCount: frameCount,
            HasAlpha: header.AlphaType != SKAlphaType.Opaque,
            ColorDepth: header.BitsPerPixel,
            // EncodedOrigin's values are the Exif orientation values; top-left is "nothing to apply".
            ExifOrientation: origin == SKEncodedOrigin.TopLeft ? null : (int)origin,
            HasIccProfile: TaggedColorSpace(codec) is not null);
    }

    private static string? ReadableSlug(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => "jpeg",
        SKEncodedImageFormat.Png => "png",
        SKEncodedImageFormat.Webp => "webp",
        SKEncodedImageFormat.Gif => "gif",
        SKEncodedImageFormat.Bmp => "bmp",
        SKEncodedImageFormat.Ico => "ico",
        SKEncodedImageFormat.Wbmp => "wbmp",
        _ => null,
    };

    /// <summary>The source's color space when it is one worth carrying; sRGB and untagged are the same picture.</summary>
    private static SKColorSpace? TaggedColorSpace(SKCodec codec) =>
        codec.Info.ColorSpace is { IsSrgb: false } tagged ? tagged : null;

    private static SKBitmap DecodeFrame(SKCodec codec, SKColorSpace? colorSpace, SKCodecOptions options)
    {
        var bitmap = RasterImage.Allocate(codec.Info.Width, codec.Info.Height, colorSpace);
        DecodeInto(codec, bitmap, options);
        return bitmap;
    }

    private static void DecodeInto(SKCodec codec, SKBitmap bitmap, SKCodecOptions options)
    {
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), options);
        if (result != SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new MediaDecodeException($"Source bytes failed to decode: {result}.");
        }
    }
}
