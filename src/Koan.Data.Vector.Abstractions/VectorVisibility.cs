namespace Koan.Data.Vector.Abstractions;

/// <summary>The visibility guarantee after an awaited vector mutation.</summary>
public enum VectorVisibility
{
    Session,
    Eventual
}
