namespace Koan.Rag.Abstractions;

/// <summary>
/// Options for a corpus rebuild (full re-ingestion) operation.
/// </summary>
public sealed record RagRebuildOptions
{
    /// <summary>
    /// New directive to apply during rebuild. If set, all documents are
    /// re-extracted with the new directive. Requires <see cref="Confirm"/> = true.
    /// </summary>
    public string? Directive { get; init; }

    /// <summary>
    /// Must be true when <see cref="Directive"/> is set, to acknowledge
    /// the destructive cost of re-extraction. Throws if directive is set
    /// but confirm is false.
    /// </summary>
    public bool Confirm { get; init; }
}
