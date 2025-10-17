using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using Koan.Media.Core.Model;
using Koan.Media.Abstractions;
using Koan.Media.Abstractions.Model;

namespace Koan.Media.Core.Extensions;

/// <summary>
/// Fluent transformation API for image streams
/// Provides immediate execution with automatic stream disposal
/// </summary>
public static class StreamTransformExtensions
{
    // ======================================
    // RESIZE OPERATIONS
    // ======================================

    /// <summary>
    /// Resize to exact dimensions (may distort aspect ratio)
    /// Example: Resize(300, 200) → 300×200 regardless of original aspect
    /// </summary>
    public static async Task<Stream> Resize(this Stream source, int width, int height, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.Resize(width, height));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Resize by scale factors (double for relative)
    /// Example: Resize(0.5, 0.5) → 50% width × 50% height
    /// Example: Resize(0.5, 1.0) → 50% width × 100% height (skinny)
    /// </summary>
    public static async Task<Stream> Resize(this Stream source, double scaleX, double scaleY, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            var targetWidth = (int)(image.Width * scaleX);
            var targetHeight = (int)(image.Height * scaleY);

            image.Mutate(x => x.Resize(targetWidth, targetHeight));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Resize to fit within dimensions (preserves aspect ratio, no cropping)
    /// Example: 1000×500 → ResizeFit(800, 600) → 800×400 (fits within bounds)
    /// </summary>
    public static async Task<Stream> ResizeFit(this Stream source, int maxWidth, int maxHeight, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxWidth, maxHeight),
                Mode = ResizeMode.Max
            }));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Resize to cover dimensions (preserves aspect ratio, crops overflow)
    /// Example: 1000×500 → ResizeCover(800, 800, CropFrom.Center) → 800×800 (crops sides)
    /// </summary>
    public static async Task<Stream> ResizeCover(this Stream source, int width, int height, CropFrom anchor = CropFrom.Center, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Crop,
                Position = ToAnchorPosition(anchor)
            }));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Resize by width, calculate height to preserve aspect ratio
    /// Example: 1000×500 → ResizeX(500) → 500×250 (factor: 0.5, applied to both axes)
    /// </summary>
    public static async Task<Stream> ResizeX(this Stream source, int targetWidth, CancellationToken ct = default)
    {
        try
        {
            // Peek dimensions without loading full image
            source.Position = 0;
            var info = await Image.IdentifyAsync(source, ct);
            source.Position = 0;

            var scaleFactor = targetWidth / (double)info.Width;
            var targetHeight = (int)(info.Height * scaleFactor);

            // Delegate to standard resize
            return await source.Resize(targetWidth, targetHeight, ct);
        }
        catch
        {
            await source.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Resize by height, calculate width to preserve aspect ratio
    /// Example: 1000×500 → ResizeY(250) → 500×250 (factor: 0.5, applied to both axes)
    /// </summary>
    public static async Task<Stream> ResizeY(this Stream source, int targetHeight, CancellationToken ct = default)
    {
        try
        {
            source.Position = 0;
            var info = await Image.IdentifyAsync(source, ct);
            source.Position = 0;

            var scaleFactor = targetHeight / (double)info.Height;
            var targetWidth = (int)(info.Width * scaleFactor);

            return await source.Resize(targetWidth, targetHeight, ct);
        }
        catch
        {
            await source.DisposeAsync();
            throw;
        }
    }

    // ======================================
    // CROP OPERATIONS
    // ======================================

    /// <summary>
    /// Crop to absolute pixel coordinates
    /// Example: Crop(100, 100, 300, 200) → 300×200 region starting at (100,100)
    /// </summary>
    public static async Task<Stream> Crop(this Stream source, int x, int y, int width, int height, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(img => img.Crop(new SixLabors.ImageSharp.Rectangle(x, y, width, height)));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Crop to rectangle
    /// </summary>
    public static Task<Stream> Crop(this Stream source, Model.Rectangle rect, CancellationToken ct = default)
    {
        return source.Crop(rect.X, rect.Y, rect.Width, rect.Height, ct);
    }

    /// <summary>
    /// Crop centered region
    /// Example: 1000×500 → CropCenter(400, 400) → 400×400 square from center
    /// </summary>
    public static async Task<Stream> CropCenter(this Stream source, int width, int height, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);

            var x = (image.Width - width) / 2;
            var y = (image.Height - height) / 2;

            image.Mutate(img => img.Crop(new SixLabors.ImageSharp.Rectangle(x, y, width, height)));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Crop to aspect ratio (e.g., 16:9, 4:3, 1:1)
    /// Example: 1000×500 → Crop(1.0) → 500×500 (square from center)
    /// Example: 1000×500 → Crop(16.0/9.0) → 1000×562.5 (16:9 from center)
    /// Example: 1000×1000 → Crop(16.0/9.0, CropFrom.Top) → 1000×562.5 (16:9 from top)
    /// </summary>
    public static async Task<Stream> Crop(this Stream source, double aspectRatio, CropFrom anchor = CropFrom.Center, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);

            var currentAspect = image.Width / (double)image.Height;
            int cropWidth, cropHeight, cropX, cropY;

            if (currentAspect > aspectRatio)
            {
                // Image is wider than target aspect - crop width
                cropHeight = image.Height;
                cropWidth = (int)(cropHeight * aspectRatio);
                cropY = 0;
                cropX = CalculateAnchorOffset(anchor, image.Width, cropWidth, isHorizontal: true);
            }
            else
            {
                // Image is taller than target aspect - crop height
                cropWidth = image.Width;
                cropHeight = (int)(cropWidth / aspectRatio);
                cropX = 0;
                cropY = CalculateAnchorOffset(anchor, image.Height, cropHeight, isHorizontal: false);
            }

            image.Mutate(img => img.Crop(new SixLabors.ImageSharp.Rectangle(cropX, cropY, cropWidth, cropHeight)));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Crop to square (convenience for 1:1 aspect ratio)
    /// Equivalent to: Crop(1.0, anchor)
    /// Example: 1000×500 → CropSquare() → 500×500 from center
    /// </summary>
    public static Task<Stream> CropSquare(this Stream source, CropFrom anchor = CropFrom.Center, CancellationToken ct = default)
    {
        return source.Crop(1.0, anchor, ct);
    }

    // ======================================
    // PADDING OPERATIONS
    // ======================================

    /// <summary>
    /// Pad to exact dimensions with color fill
    /// Example: 500×500 → Pad(800, 600, Color.Black) → 800×600 with black bars
    /// </summary>
    public static async Task<Stream> Pad(this Stream source, int width, int height, Color? color = null, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            var padColor = color ?? Color.Transparent;

            image.Mutate(x => x.Pad(width, height, padColor));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Pad to constraint (e.g., square, 16:9 letterbox)
    /// Example: 1000×500 → Pad(PadTo.Square, Color.Black) → 1000×1000 with black bars
    /// </summary>
    public static async Task<Stream> Pad(this Stream source, PadTo constraint, Color? color = null, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            var padColor = color ?? Color.Transparent;

            var (targetWidth, targetHeight) = CalculatePadDimensions(image.Width, image.Height, constraint);

            image.Mutate(x => x.Pad(targetWidth, targetHeight, padColor));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    // ======================================
    // ROTATION & ORIENTATION
    // ======================================

    /// <summary>
    /// Auto-orient using EXIF metadata
    /// </summary>
    public static async Task<Stream> AutoOrient(this Stream source, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.AutoOrient());

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Rotate by degrees (clockwise)
    /// Example: Rotate(90) → 90° clockwise rotation
    /// </summary>
    public static async Task<Stream> Rotate(this Stream source, int degrees, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.Rotate(degrees));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Flip horizontally (mirror)
    /// </summary>
    public static async Task<Stream> FlipHorizontal(this Stream source, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.Flip(FlipMode.Horizontal));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Flip vertically
    /// </summary>
    public static async Task<Stream> FlipVertical(this Stream source, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.Flip(FlipMode.Vertical));

            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = 90 }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    // ======================================
    // FORMAT & QUALITY
    // ======================================

    /// <summary>
    /// Convert image format
    /// Supported formats: jpeg, jpg, png, webp
    /// Example: ConvertFormat("webp", 85)
    /// </summary>
    public static async Task<Stream> ConvertFormat(this Stream source, string format, int quality = 85, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            var result = new MemoryStream();

            switch (format.ToLowerInvariant())
            {
                case "jpeg":
                case "jpg":
                    await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = quality }, ct);
                    break;
                case "png":
                    await image.SaveAsPngAsync(result, ct);
                    break;
                case "webp":
                    await image.SaveAsWebpAsync(result, new WebpEncoder { Quality = quality }, ct);
                    break;
                default:
                    throw new NotSupportedException($"Format '{format}' is not supported. Supported formats: jpeg, jpg, png, webp");
            }

            result.Position = 0;
            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Optimize quality without format conversion
    /// </summary>
    public static async Task<Stream> OptimizeQuality(this Stream source, int quality, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = quality }, ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    // ======================================
    // MATERIALIZATION & STORAGE
    // ======================================

    /// <summary>
    /// Materialize transformation result for branching/reuse
    /// Returns TransformResult that can be branched multiple times
    /// </summary>
    public static async Task<TransformResult> Result(this Stream source, CancellationToken ct = default)
    {
        // Stream will be disposed by TransformResult constructor
        return await Task.FromResult(new TransformResult(source, "image/jpeg"));
    }

    /// <summary>
    /// Store transformed stream as new MediaEntity
    /// Automatically disposes stream after upload
    /// NOTE: Users should call MediaEntity.Upload() or Onboard() directly with the transformed stream
    /// Example: var entity = await PhotoAsset.Upload(transformedStream, "photo.jpg", "image/jpeg");
    /// </summary>
    /// <remarks>
    /// This is a placeholder for future enhancement.
    /// Cannot call static methods on generic type parameters without reflection.
    /// For now, use: var bytes = await stream.ToBytes(); then MediaEntity.Upload()
    /// </remarks>
    [Obsolete("Use MediaEntity<T>.Upload() or Onboard() directly with transformed stream", false)]
    public static Task<string> StoreAs<TEntity>(this Stream source, CancellationToken ct = default)
        where TEntity : MediaEntity<TEntity>, new()
    {
        throw new NotImplementedException(
            "Use MediaEntity<T>.Upload() or Onboard() directly. " +
            "Example: var entity = await PhotoAsset.Upload(transformedStream, \"photo.jpg\", \"image/jpeg\");"
        );
    }

    /// <summary>
    /// Get transformed stream as byte array (for in-memory processing)
    /// </summary>
    public static async Task<byte[]> ToBytes(this Stream source, CancellationToken ct = default)
    {
        try
        {
            using var ms = new MemoryStream();
            await source.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    /// <summary>
    /// Get transformed stream as base64 string (for embedding)
    /// </summary>
    public static async Task<string> ToBase64(this Stream source, CancellationToken ct = default)
    {
        var bytes = await source.ToBytes(ct);
        return Convert.ToBase64String(bytes);
    }

    // ======================================
    // HELPER METHODS
    // ======================================

    private static AnchorPositionMode ToAnchorPosition(CropFrom anchor) => anchor switch
    {
        CropFrom.Center => AnchorPositionMode.Center,
        CropFrom.Top => AnchorPositionMode.Top,
        CropFrom.Bottom => AnchorPositionMode.Bottom,
        CropFrom.Left => AnchorPositionMode.Left,
        CropFrom.Right => AnchorPositionMode.Right,
        CropFrom.TopLeft => AnchorPositionMode.TopLeft,
        CropFrom.TopRight => AnchorPositionMode.TopRight,
        CropFrom.BottomLeft => AnchorPositionMode.BottomLeft,
        CropFrom.BottomRight => AnchorPositionMode.BottomRight,
        _ => AnchorPositionMode.Center
    };

    private static int CalculateAnchorOffset(CropFrom anchor, int totalSize, int cropSize, bool isHorizontal)
    {
        var excess = totalSize - cropSize;

        return anchor switch
        {
            CropFrom.Center => excess / 2,
            CropFrom.Top when !isHorizontal => 0,
            CropFrom.Bottom when !isHorizontal => excess,
            CropFrom.Left when isHorizontal => 0,
            CropFrom.Right when isHorizontal => excess,
            CropFrom.TopLeft => 0,
            CropFrom.TopRight when isHorizontal => excess,
            CropFrom.BottomLeft when !isHorizontal => excess,
            CropFrom.BottomRight => excess,
            _ => excess / 2  // Default to center
        };
    }

    private static (int width, int height) CalculatePadDimensions(int currentWidth, int currentHeight, PadTo constraint)
    {
        return constraint switch
        {
            PadTo.Square => (Math.Max(currentWidth, currentHeight), Math.Max(currentWidth, currentHeight)),
            PadTo.Landscape_16_9 => CalculateForAspectRatio(currentWidth, currentHeight, 16.0 / 9.0),
            PadTo.Portrait_9_16 => CalculateForAspectRatio(currentWidth, currentHeight, 9.0 / 16.0),
            PadTo.Landscape_4_3 => CalculateForAspectRatio(currentWidth, currentHeight, 4.0 / 3.0),
            PadTo.Portrait_3_4 => CalculateForAspectRatio(currentWidth, currentHeight, 3.0 / 4.0),
            PadTo.Landscape_21_9 => CalculateForAspectRatio(currentWidth, currentHeight, 21.0 / 9.0),
            _ => (currentWidth, currentHeight)
        };
    }

    private static (int width, int height) CalculateForAspectRatio(int currentWidth, int currentHeight, double targetAspect)
    {
        var currentAspect = currentWidth / (double)currentHeight;

        if (Math.Abs(currentAspect - targetAspect) < 0.01)
            return (currentWidth, currentHeight);

        if (currentAspect > targetAspect)
        {
            // Image is wider - pad height
            var targetHeight = (int)(currentWidth / targetAspect);
            return (currentWidth, targetHeight);
        }
        else
        {
            // Image is taller - pad width
            var targetWidth = (int)(currentHeight * targetAspect);
            return (targetWidth, currentHeight);
        }
    }
}
