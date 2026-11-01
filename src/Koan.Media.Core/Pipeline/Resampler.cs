using System.Buffers;
using System.Collections.Concurrent;
using SkiaSharp;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Separable Catmull-Rom scaled to the reduction, over premultiplied pixels. Skia's own samplers do not widen
/// their kernel when reducing and alias on fine detail, so every resize in the pipeline goes through here.
/// </summary>
/// <remarks>
/// Output rows are processed in chunks in parallel. Each chunk holds a window of horizontally resampled source
/// rows, at most one kernel tall: the buffer is <c>16 × target width × kernel rows</c> bytes per worker, however
/// tall the source is. A box computes only the pixels it keeps of the scaled picture, with the same result as
/// scaling all of it and cropping, so a cover at an extreme aspect allocates the output and no more.
/// </remarks>
internal static class Resampler
{
    private const int Channels = 4;
    private const double Radius = 2.0;

    public static SKBitmap Resize(SKBitmap source, int width, int height) =>
        Resize(source, width, height, SKRectI.Create(0, 0, width, height));

    /// <summary>The <paramref name="box"/> of <paramref name="source"/> scaled to <paramref name="scaledWidth"/> by <paramref name="scaledHeight"/>.</summary>
    public static SKBitmap Resize(SKBitmap source, int scaledWidth, int scaledHeight, SKRectI box)
    {
        var (width, height) = (box.Width, box.Height);
        var target = RasterImage.Allocate(width, height, source.ColorSpace);
        try
        {
            var (xStart, xWeights) = Contributions(source.Width, scaledWidth, box.Left, width);
            var (yStart, yWeights) = Contributions(source.Height, scaledHeight, box.Top, height);
            var windowRows = yWeights.Max(w => w.Length);
            var chunk = Math.Max(16, height / Math.Max(1, Environment.ProcessorCount * 2));

            Parallel.ForEach(Partitioner.Create(0, height, chunk), range =>
            {
                var windowLength = windowRows * width * Channels;
                var window = ArrayPool<float>.Shared.Rent(windowLength);
                var sum = ArrayPool<float>.Shared.Rent(width * Channels);
                var heldRow = new int[windowRows];
                Array.Fill(heldRow, -1);
                try
                {
                    ReadOnlySpan<byte> pixels = source.GetPixelSpan();
                    var output = target.GetPixelSpan();
                    for (var y = range.Item1; y < range.Item2; y++)
                    {
                        Array.Clear(sum, 0, width * Channels);
                        var weights = yWeights[y];
                        for (var j = 0; j < weights.Length; j++)
                        {
                            var sourceRow = yStart[y] + j;
                            var slot = sourceRow % windowRows;
                            var row = window.AsSpan(slot * width * Channels, width * Channels);
                            if (heldRow[slot] != sourceRow)
                            {
                                ResampleRow(pixels.Slice(sourceRow * source.RowBytes, source.Width * Channels), row, xStart, xWeights);
                                heldRow[slot] = sourceRow;
                            }
                            var weight = weights[j];
                            for (var i = 0; i < row.Length; i++) sum[i] += weight * row[i];
                        }
                        WriteRow(sum, output.Slice(y * target.RowBytes, width * Channels));
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(window);
                    ArrayPool<float>.Shared.Return(sum);
                }
            });
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    private static void ResampleRow(ReadOnlySpan<byte> source, Span<float> row, int[] start, float[][] weights)
    {
        for (var x = 0; x < weights.Length; x++)
        {
            var kernel = weights[x];
            var offset = start[x] * Channels;
            float r = 0, g = 0, b = 0, a = 0;
            for (var j = 0; j < kernel.Length; j++, offset += Channels)
            {
                var weight = kernel[j];
                r += weight * source[offset];
                g += weight * source[offset + 1];
                b += weight * source[offset + 2];
                a += weight * source[offset + 3];
            }
            var at = x * Channels;
            row[at] = r;
            row[at + 1] = g;
            row[at + 2] = b;
            row[at + 3] = a;
        }
    }

    private static void WriteRow(float[] sum, Span<byte> output)
    {
        for (var i = 0; i < output.Length; i += Channels)
        {
            var alpha = ToByte(sum[i + 3]);
            // The kernel's negative lobes can push a color past its alpha, which is not a valid premultiplied pixel.
            output[i] = Math.Min(ToByte(sum[i]), alpha);
            output[i + 1] = Math.Min(ToByte(sum[i + 1]), alpha);
            output[i + 2] = Math.Min(ToByte(sum[i + 2]), alpha);
            output[i + 3] = alpha;
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(value + 0.5f, 0f, 255f);

    private static (int[] Start, float[][] Weights) Contributions(int source, int target, int first, int count)
    {
        var ratio = (double)source / target;
        var scale = Math.Max(ratio, 1.0);
        var radius = Radius * scale;
        var start = new int[count];
        var weights = new float[count][];
        for (var i = 0; i < count; i++)
        {
            var center = (first + i + 0.5) * ratio - 0.5;
            var low = Math.Max(0, (int)Math.Ceiling(center - radius));
            var high = Math.Min(source - 1, (int)Math.Floor(center + radius));
            var kernel = new float[high - low + 1];
            double total = 0;
            for (var j = low; j <= high; j++)
            {
                var value = CatmullRom(Math.Abs((j - center) / scale));
                kernel[j - low] = (float)value;
                total += value;
            }
            for (var j = 0; j < kernel.Length; j++) kernel[j] = (float)(kernel[j] / total);
            start[i] = low;
            weights[i] = kernel;
        }
        return (start, weights);
    }

    private static double CatmullRom(double x) => x switch
    {
        < 1 => 1.5 * x * x * x - 2.5 * x * x + 1,
        < 2 => -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2,
        _ => 0,
    };
}
