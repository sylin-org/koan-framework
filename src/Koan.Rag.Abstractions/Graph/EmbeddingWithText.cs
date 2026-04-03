namespace Koan.Rag.Abstractions;

/// <summary>
/// An embedding vector paired with its source chunk/node ID and the original text.
/// The text is needed by the distillation tree builder for summarization at Level 1.
/// <para>
/// Note: the <c>Embedding</c> array uses reference equality in record comparisons.
/// Do not use this type as a dictionary key or in HashSet operations.
/// </para>
/// </summary>
public sealed record EmbeddingWithText(string Id, float[] Embedding, string Text);
