namespace Koan.Data.Abstractions;

/// <summary>Provider-neutral outcome of one Entity mutation.</summary>
public enum MutationOutcome
{
    Inserted,
    Updated,
    Deleted,
    Missing,
    Conflict
}
