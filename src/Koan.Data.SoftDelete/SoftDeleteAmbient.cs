namespace Koan.Data.SoftDelete;

/// <summary>
/// The ambient "show soft-deleted rows" slice. Inside a <c>using (T.WithDeleted())</c> scope the soft-delete read
/// filter is suppressed, so a query (or a load-before-<c>.Restore()</c>) sees the deleted rows. Off by default — the
/// recycle bin is opt-in per scope, never globally on.
/// </summary>
internal static class SoftDeleteAmbient
{
    private static readonly AsyncLocal<bool> _showDeleted = new();

    /// <summary>Whether deleted rows are visible in the current ambient.</summary>
    public static bool ShowDeleted => _showDeleted.Value;

    /// <summary>Enter a scope where reads include soft-deleted rows; dispose restores the previous state.</summary>
    public static IDisposable Enter()
    {
        var prev = _showDeleted.Value;
        _showDeleted.Value = true;
        return new Scope(prev);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _showDeleted.Value = previous;
        }
    }
}
