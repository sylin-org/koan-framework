namespace Koan.AI.Connector.Onnx;

/// <summary>
/// Options for the in-process ONNX embedding adapter. The model is side-loadable: point
/// <see cref="ModelPath"/> at a local sentence-embedding ONNX file (e.g. all-MiniLM-L6-v2) and
/// <see cref="VocabPath"/> at its WordPiece <c>vocab.txt</c> (defaults to <c>vocab.txt</c> next to the model).
/// Nothing is downloaded at runtime — air-gap friendly by construction.
/// </summary>
public sealed class OnnxOptions
{
    public const string Section = "Koan:Ai:Onnx";

    /// <summary>Path to the ONNX model file. When unset, the adapter does not register (no model = nothing to embed).</summary>
    public string? ModelPath { get; set; }

    /// <summary>Path to the WordPiece vocabulary. Defaults to <c>vocab.txt</c> beside the model.</summary>
    public string? VocabPath { get; set; }

    /// <summary>Reporting name surfaced to VectorModelGuard and the boot report.</summary>
    public string ModelName { get; set; } = "all-MiniLM-L6-v2";

    /// <summary>Maximum tokens per input (truncation length).</summary>
    public int MaxTokens { get; set; } = 256;

    /// <summary>Lowercase input before tokenizing (true for uncased models like all-MiniLM).</summary>
    public bool LowercaseInput { get; set; } = true;

    /// <summary>L2-normalize the pooled embedding (sentence-transformers convention; cosine-ready).</summary>
    public bool NormalizeEmbeddings { get; set; } = true;

    /// <summary>Fallback dimension when it can't be read from the model's output metadata.</summary>
    public int Dimension { get; set; } = 384;
}
