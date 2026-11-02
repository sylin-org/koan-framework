namespace Koan.Data.Core.Commands;

/// <summary>What <see cref="CommandQueue.Committed"/> announces: the command that committed a write, and the last number it took from each sequence.</summary>
public sealed record CommandCommitted(ICommand Command, IReadOnlyDictionary<Sequence, long> Sequences);
