using System;
using System.Threading;

namespace Koan.Storage.Keys;

/// <summary>
/// Carries a storage operation's semantic subject through the type-erased <see cref="ScopedStorageService"/>
/// boundary. The Storage identity compiler uses that type to apply the shared segmentation plan, including a
/// subject's <c>[HostScoped]</c> applicability. A raw <c>IStorageService</c> operation has no subject and isolates
/// by default. Infrastructure can deliberately declare an explicit host operation.
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

    /// <summary>Enter an explicit control-plane scope whose physical storage key is not segmented.</summary>
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
