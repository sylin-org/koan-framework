namespace Koan.Data.Abstractions;

/// <summary>One ordered provider-neutral mutation outcome for a keyed batch item.</summary>
public sealed record BatchItemResult<TKey>(int Index, TKey Id, MutationOutcome Outcome)
    where TKey : notnull;
