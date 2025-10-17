using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Helper for creating test images programmatically
/// </summary>
public static class TestImageHelper
{
    /// <summary>
    /// Create a test image with specified dimensions and color
    /// </summary>
    public static Stream CreateTestImage(int width, int height, Color? color = null)
    {
        var fillColor = color ?? Color.Blue;
        var image = new Image<Rgba32>(width, height);

        image.Mutate(x => x.Fill(fillColor));

        // Add dimension text for debugging
        image.Mutate(x => x.DrawText(
            $"{width}×{height}",
            SystemFonts.CreateFont("Arial", 20),
            Color.White,
            new PointF(10, 10)
        ));

        var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = 90 });
        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// Create a test image with gradient for visual verification
    /// </summary>
    public static Stream CreateGradientImage(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);

        // Create horizontal gradient
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var ratio = x / (float)width;
                var color = new Rgba32(
                    (byte)(255 * ratio),
                    (byte)(128 * (1 - ratio)),
                    (byte)(255 * (1 - ratio))
                );
                image[x, y] = color;
            }
        }

        var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder { Quality = 90 });
        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// Get dimensions of a stream without disposing it
    /// </summary>
    public static async Task<(int width, int height)> GetDimensions(Stream stream)
    {
        var originalPosition = stream.Position;
        stream.Position = 0;

        var info = await Image.IdentifyAsync(stream);
        stream.Position = originalPosition;

        return (info.Width, info.Height);
    }

    /// <summary>
    /// Clone a stream for testing
    /// </summary>
    public static Stream CloneStream(Stream source)
    {
        var clone = new MemoryStream();
        source.Position = 0;
        source.CopyTo(clone);
        source.Position = 0;
        clone.Position = 0;
        return clone;
    }
}
