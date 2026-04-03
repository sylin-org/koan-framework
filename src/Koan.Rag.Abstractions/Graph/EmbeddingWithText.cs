namespace Koan.Rag.Abstractions;

/// <summary>
/// An embedding vector paired with its source chunk/node ID and the original text.
/// The text is needed by the distillation tree builder for summarization at Level 1.
/// </summary>
public sealed record EmbeddingWithText(string Id, float[] Embedding, string Text);
