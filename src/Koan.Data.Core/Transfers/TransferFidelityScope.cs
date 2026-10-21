namespace Koan.Data.Core.Transfers;

/// <summary>
/// The ambient flag an exact transfer raises around its own reads and writes (DATA-0149). The repository facade reads it
/// to leave Lifecycle out, and the write-stamp plan reads it to apply identity only. Nothing else changes: source policy,
/// guards, tenancy and isolation, field transforms and receipts are the facade's ordinary path.
/// </summary>
internal static class TransferFidelityScope
{
    private static readonly AsyncLocal<bool> Exact = new();

    /// <summary>Whether the current flow is an exact transfer's own read or write.</summary>
    public static bool IsExact => Exact.Value;

    /// <summary>Sets the flag for the current flow until the returned scope is disposed.</summary>
    public static Scope Enter(bool exact)
    {
        var previous = Exact.Value;
        Exact.Value = exact;
        return new Scope(previous);
    }

    public readonly struct Scope(bool previous) : IDisposable
    {
        public void Dispose() => Exact.Value = previous;
    }
}
