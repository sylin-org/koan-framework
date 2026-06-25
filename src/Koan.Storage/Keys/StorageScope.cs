using System;
using System.Threading;

namespace Koan.Storage.Keys;

/// <summary>
/// STOR-0011 §2: the ambient that carries the current blob op's entity TYPE — so the type-erased
/// <see cref="ScopedStorageService"/> decorator can apply <c>ManagedFieldRegistry.ForType</c> + the
/// <c>[HostScoped]</c> exemption + the typed <c>IStorageGuard</c> — or an explicit HOST-SCOPE flag (infra opting
/// out of isolation). When no scope is set, the decorator falls back to the type-less ambient axis bag (the
/// fail-safe: a raw <c>IStorageService</c> caller isolates by default).
/// </summary>
public static class StorageScope
{
    private static readonly AsyncLocal<Frame?> _current = new();

    internal static Type? CurrentType => _current.Value?.EntityType;
    internal static bool IsHostScope => _current.Value?.HostScope ?? false;

    /// <summary>Enter a typed storage scope for the lifetime of the op (the type-aware layers set this).</summary>
    public static IDisposable For(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Push(new Frame(entityType, false));
    }

    /// <summary>Enter an explicit host scope — the op is unprefixed and unguarded (the <c>IAmbientExempt</c> analog).</summary>
    public static IDisposable HostScoped() => Push(new Frame(null, true));

    private static IDisposable Push(Frame f)
    {
        var prev = _current.Value;
        _current.Value = f;
        return new Pop(prev);
    }

    private sealed record Frame(Type? EntityType, bool HostScope);

    private sealed class Pop : IDisposable
    {
        private readonly Frame? _prev;
        private bool _done;
        public Pop(Frame? prev) => _prev = prev;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _current.Value = _prev;
        }
    }
}
