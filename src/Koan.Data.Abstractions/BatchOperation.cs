namespace Koan.Data.Abstractions;

/// <summary>The logical operation represented by one batch outcome.</summary>
public enum BatchOperation
{
    Add,
    Update,
    Mutate,
    Delete
}
