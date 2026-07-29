namespace Koan.Data.Abstractions;

/// <summary>
/// Ordered outcome for one queued batch operation. <paramref name="Index"/> is the zero-based
/// position in the logical batch and deliberately avoids placing an Entity identity in diagnostics.
/// </summary>
public sealed record BatchItemResult(int Index, BatchOperation Operation, BatchItemOutcome Outcome);
