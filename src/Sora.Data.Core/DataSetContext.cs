namespace Sora.Data.Core;

/// <summary>
/// Ambient context to route data operations to a logical "set" (e.g., root, backup).
/// Providers that compute storage names must consult this context indirectly via
/// StorageNameRegistry.
/// </summary>
public static class DataSetContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    /// <summary>
    /// Enter a scoped set value. Dispose to restore the previous value.
    /// </summary>
    public static IDisposable With(string? set)
    {
        var prev = _current.Value;
        _current.Value = set;
        return new Pop(() => _current.Value = prev);
    }

    private sealed class Pop(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() { _action?.Invoke(); _action = null; }
    }
}
