using System.Collections.Immutable;
using System.Threading;

namespace Koan.Core.Context;

/// <summary>
/// The typed context of the current logical execution flow. Values are exact-type keyed, immutable snapshots;
/// scopes flow through <c>await</c> and child execution contexts without becoming host-owned global state.
/// </summary>
/// <remarks>
/// Context values should themselves be immutable. Do not place service providers, scoped services, or disposable
/// resources here; host-owned dependencies belong in dependency injection and are deliberately not carried.
/// </remarks>
public static class KoanContext
{
    private static readonly AsyncLocal<ImmutableDictionary<Type, object>?> CurrentState = new();

    /// <summary>Gets the value of exact type <typeparamref name="T"/>, or <c>null</c> when it is absent.</summary>
    public static T? Get<T>() where T : class
        => CurrentState.Value?.TryGetValue(typeof(T), out var value) == true ? (T)value : null;

    /// <summary>
    /// Pushes <paramref name="value"/> for the current logical flow. Disposing the returned scope restores the exact
    /// prior context snapshot without changing sibling flows.
    /// </summary>
    public static IDisposable Push<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var previous = CurrentState.Value;
        CurrentState.Value = (previous ?? ImmutableDictionary<Type, object>.Empty).SetItem(typeof(T), value);
        return new RestoreScope(previous);
    }

    /// <summary>
    /// Explicitly removes the value of type <typeparamref name="T"/> for the current logical flow. Disposing restores
    /// the prior snapshot. Suppressing an already-absent value is allocation-free.
    /// </summary>
    public static IDisposable Suppress<T>() where T : class
    {
        var previous = CurrentState.Value;
        if (previous is null || !previous.ContainsKey(typeof(T))) return NoopScope.Instance;

        var next = previous.Remove(typeof(T));
        CurrentState.Value = next.Count == 0 ? null : next;
        return new RestoreScope(previous);
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();
        public void Dispose() { }
    }

    private sealed class RestoreScope(ImmutableDictionary<Type, object>? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CurrentState.Value = previous;
        }
    }
}
