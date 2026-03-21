using Koan.AI.Contracts.Shared;

namespace Koan.AI.Models;

/// <summary>
/// Converts models between formats (SafeTensors → GGUF, SafeTensors → ONNX, etc.).
/// Implementations are discovered via Reference = Intent — add the converter package,
/// get the capability.
/// </summary>
public interface IFormatConverter
{
    /// <summary>Source formats this converter can read.</summary>
    ModelFormat[] SourceFormats { get; }

    /// <summary>Target formats this converter can produce.</summary>
    ModelFormat[] TargetFormats { get; }

    /// <summary>Whether this converter supports quantization during conversion.</summary>
    bool SupportsQuantization { get; }

    /// <summary>
    /// Convert a model from one format to another.
    /// Returns the path to the converted model file.
    /// </summary>
    Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed record ConversionRequest
{
    /// <summary>Path to the source model.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Source format.</summary>
    public required ModelFormat SourceFormat { get; init; }

    /// <summary>Target format.</summary>
    public required ModelFormat TargetFormat { get; init; }

    /// <summary>Output directory.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Quantization to apply during conversion (optional).</summary>
    public Quantization Quantization { get; init; } = Quantization.None;

    /// <summary>ONNX optimization level (for ONNX target only).</summary>
    public int OnnxOptimizationLevel { get; init; }
}

public sealed record ConversionResult
{
    public required string OutputPath { get; init; }
    public required ModelFormat Format { get; init; }
    public Quantization Quantization { get; init; }
    public long FileSizeBytes { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed record ConversionProgress
{
    public string Phase { get; init; } = string.Empty;
    public double Percent { get; init; }
    public string? Message { get; init; }
}
