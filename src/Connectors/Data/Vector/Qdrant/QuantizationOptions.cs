namespace Koan.Data.Vector.Connector.Qdrant;

/// <summary>
/// Per-collection vector quantization configuration. Qdrant supports three modes, each
/// trading recall fidelity for memory footprint:
///
/// <para>
/// <b>Scalar</b> (the Koan default) — compresses float32 → uint8, ~4× memory reduction.
/// Rescoring against the original vectors keeps recall@10 within ~1-3% of native precision
/// on most embedding models. Best general-purpose choice for lean deployments.
/// </para>
///
/// <para>
/// <b>Product</b> — codebook-based compression configurable from x4 to x64. Larger recall
/// hit than scalar but proportionally larger memory wins. Suitable for very large collections
/// where memory is the dominant constraint.
/// </para>
///
/// <para>
/// <b>Binary</b> — 1-bit-per-dimension compression, ~32× memory reduction. Significant recall
/// loss without rescoring; only viable for very high-dimensional embeddings where the recall
/// drop is recoverable via aggressive oversampling + rescore.
/// </para>
///
/// <para>
/// <b>None</b> — disables quantization. Vectors are stored at native float32 precision.
/// Use when fidelity matters more than memory (e.g. semantic search over small corpora).
/// </para>
///
/// <para>
/// Search-time settings (<see cref="Rescore"/>, <see cref="Oversampling"/>) only apply when
/// a quantization mode is active; they have no effect when <see cref="Type"/> is "None".
/// </para>
/// </summary>
public sealed class QuantizationOptions
{
    /// <summary>
    /// Quantization mode. Valid values: <c>Scalar</c> (default, recommended), <c>Product</c>,
    /// <c>Binary</c>, <c>None</c>. Case-insensitive.
    /// </summary>
    public string Type { get; set; } = "Scalar";

    /// <summary>
    /// Keep the compressed quantization codebook in RAM. Default true — fast search path.
    /// Set false to push the codebook to disk for the deepest memory savings (at the cost of
    /// per-query I/O).
    /// </summary>
    public bool AlwaysRam { get; set; } = true;

    /// <summary>
    /// Scalar mode only: quantile cutoff for outlier trimming when computing the value
    /// distribution. Default 0.99 — clip the top/bottom 0.5% before binning into uint8.
    /// Higher values preserve more outliers (higher fidelity, less effective compression).
    /// Ignored for Product/Binary modes.
    /// </summary>
    public double? Quantile { get; set; } = 0.99;

    /// <summary>
    /// Product mode only: compression ratio. Valid values: <c>x4</c>, <c>x8</c>, <c>x16</c>
    /// (default), <c>x32</c>, <c>x64</c>. Higher = more compression, worse recall.
    /// Ignored for Scalar/Binary modes.
    /// </summary>
    public string? Compression { get; set; } = "x16";

    /// <summary>
    /// At search time, re-rank the top candidates using the original (unquantized) vectors.
    /// Default true — recovers most of the recall lost to quantization. Disable only if you
    /// want raw quantized-only scoring (faster but lossier).
    /// </summary>
    public bool Rescore { get; set; } = true;

    /// <summary>
    /// At search time, multiplier on TopK used to size the candidate pool before rescoring.
    /// Default 2.0 — fetch 2× the requested K from the quantized index, rescore, then return
    /// top K. Higher values trade query latency for recall.
    /// </summary>
    public double Oversampling { get; set; } = 2.0;

    /// <summary>True when this configuration represents an active quantization mode (not None).</summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Type) &&
        !string.Equals(Type.Trim(), "None", System.StringComparison.OrdinalIgnoreCase);
}
