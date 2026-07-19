namespace Koan.Data.SoftDelete;

/// <summary>
/// The ambient "show soft-deleted rows" slice. Inside a <c>using (T.WithDeleted())</c> scope the soft-delete read
/// filter is suppressed for <c>T</c>, so a query (or a load-before-<c>.Restore()</c>) sees that type's deleted rows.
/// Off by default and type-targeted — opening one recycle bin never reveals another entity type.
/// </summary>
internal static class SoftDeleteAmbient
{
    private static readonly AsyncLocal<ScopeFrame?> Current = new();

    /// <summary>Whether deleted rows for <paramref name="entityType"/> are visible in the current ambient.</summary>
    public static bool Includes(Type entityType)
    {
        for (var frame = Current.Value; frame is not null; frame = frame.Parent)
        {
            if (frame.EntityType == entityType)
                return true;
        }

        return false;
    }

    /// <summary>Enter a scope where reads of one entity type include soft-deleted rows.</summary>
    public static IDisposable Enter(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var frame = new ScopeFrame(entityType, Current.Value);
        Current.Value = frame;
        return new Scope(frame);
    }

    private sealed record ScopeFrame(Type EntityType, ScopeFrame? Parent);

    private sealed class Scope(ScopeFrame frame) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (ReferenceEquals(Current.Value, frame))
                Current.Value = frame.Parent;
        }
    }
}
