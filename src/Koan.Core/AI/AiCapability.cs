namespace Koan.Core.AI;

/// <summary>
/// Well-known AI capability identifiers. Adapters declare these to signal what they support.
/// Used by category router, model resolver, and all AI facade verbs.
/// String-based for extensibility — custom adapters can declare additional capabilities.
/// </summary>
public static class AiCapability
{
    // ── Inference ──
    public const string Chat = "Chat";
    public const string Embed = "Embed";
    public const string Ocr = "Ocr";
    public const string Vision = "Vision";
    public const string Transcribe = "Transcribe";
    public const string Quick = "Quick";
    public const string Synthesis = "Synthesis";
    public const string Thinking = "Thinking";
    public const string Tools = "Tools";
    public const string Streaming = "Streaming";
    public const string JsonMode = "JsonMode";
    public const string BatchEmbed = "BatchEmbed";

    // ── Model Lifecycle ──
    public const string Pull = "Pull";
    public const string Push = "Push";
    public const string ModelRemove = "ModelRemove";
    public const string ModelList = "ModelList";

    // ── Format Support ──
    public const string ServeGGUF = "Serve.GGUF";
    public const string ServeSafeTensors = "Serve.SafeTensors";
    public const string ServeONNX = "Serve.ONNX";
    public const string Convert = "Convert";
    public const string Quantize = "Quantize";

    // ── Training ──
    public const string Train = "Train";
    public const string Align = "Align";

    // ── Generation (AI-0033) ──
    public const string Imagine = "Imagine";
    public const string Edit = "Edit";
    public const string Render = "Render";

    // ── Audio (AI-0033) ──
    public const string Speak = "Speak";

    // ── Search (AI-0033) ──
    public const string Rerank = "Rerank";

    // ── Language (AI-0033) ──
    public const string Translate = "Translate";

    // ── Safety (AI-0033) ──
    public const string Moderate = "Moderate";

    // ── Evaluation ──
    public const string MetricCompute = "MetricCompute";
}
