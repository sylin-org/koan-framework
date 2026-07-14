namespace Koan.Core.Hosting.App;

/// <summary>
/// Ambient service provider holder for terse APIs and cross-cutting helpers.
/// </summary>
/// <remarks>
/// <para>
/// Framework hosts attach their provider for the duration of the host lifecycle. The attached
/// provider is visible to every consumer that doesn't enter an explicit scope and is released
/// when its owning host stops.
/// </para>
/// <para>
/// Tests, background jobs, and any caller that needs flow-scoped isolation use
/// <see cref="PushScope"/>. The scope's value wins over the static global for the duration of
/// the returned <see cref="IDisposable"/> and only within the current async flow — concurrent
/// tests / requests / jobs each see their own scoped value without sideways leakage.
/// </para>
/// <para>
/// This replaces the previous purely-static <c>Current</c> field, which forced test factories
/// to overwrite a shared global and triggered cross-class pollution (the kind that needed
/// <c>AggregateConfigs.Reset()</c> band-aid to fix).
/// </para>
/// </remarks>
public static class AppHost
{
    private static IServiceProvider? _global;
    private static readonly System.Threading.AsyncLocal<IServiceProvider?> _scoped = new();

    public static IServiceProvider? Current
    {
        get => _scoped.Value ?? Volatile.Read(ref _global);
        set => Interlocked.Exchange(ref _global, value);
    }

    /// <summary>
    /// Gets a required service from the current Koan host or flow scope.
    /// </summary>
    /// <remarks>
    /// This primitive supports Koan's terse framework surfaces and advanced hosting integrations.
    /// Application business code should continue to prefer constructor injection; this method is
    /// not a general-purpose service locator.
    /// </remarks>
    /// <exception cref="KoanHostContextException">
    /// Thrown when no host is active, the active provider is disposed, or the service is absent.
    /// </exception>
    public static T GetRequiredService<T>(string operation) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var services = Current;
        if (services is null)
        {
            throw new KoanHostContextException(
                operation,
                typeof(T),
                KoanHostContextException.FailureKind.MissingHost);
        }

        object? service;
        try
        {
            service = services.GetService(typeof(T));
        }
        catch (ObjectDisposedException exception)
        {
            throw new KoanHostContextException(
                operation,
                typeof(T),
                KoanHostContextException.FailureKind.DisposedHost,
                exception);
        }

        return service as T
            ?? throw new KoanHostContextException(
                operation,
                typeof(T),
                KoanHostContextException.FailureKind.MissingService);
    }

    /// <summary>
    /// Attaches <paramref name="sp"/> as the process-default provider and returns its ownership
    /// lease. Disposing the lease clears the provider only when that lease still owns the current
    /// default; it never restores an earlier provider that may already have been disposed.
    /// </summary>
    /// <remarks>
    /// This is a low-level lifecycle primitive for Koan hosting integrations. The caller must keep
    /// the returned lease alive for exactly as long as the provider is active and dispose it when
    /// that provider stops. Applications should normally use a Koan host or <c>StartKoan()</c>
    /// instead of attaching providers directly.
    /// </remarks>
    public static IDisposable Attach(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        Interlocked.Exchange(ref _global, sp);
        return new HostLease(sp);
    }

    /// <summary>
    /// Pushes <paramref name="sp"/> as the ambient provider for the duration of the returned
    /// disposable. The override is flow-scoped — visible to children of the current async
    /// context, invisible to siblings.
    /// </summary>
    public static IDisposable PushScope(IServiceProvider sp)
    {
        var previous = _scoped.Value;
        _scoped.Value = sp;
        return new Pop(previous);
    }

    private sealed class Pop : IDisposable
    {
        private readonly IServiceProvider? _previous;
        public Pop(IServiceProvider? previous) => _previous = previous;
        public void Dispose() => _scoped.Value = _previous;
    }

    private sealed class HostLease(IServiceProvider owner) : IDisposable
    {
        private IServiceProvider? _owner = owner;

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref _owner, null);
            if (currentOwner is not null)
            {
                Interlocked.CompareExchange(ref _global, null, currentOwner);
            }
        }
    }

    /// <summary>
    /// Gets the application identity owned by the current host or flow scope.
    /// </summary>
    /// <remarks>
    /// When no host is active, the process-level <see cref="global::Koan.Core.KoanEnv"/> snapshot is
    /// returned as a hostless fallback. Host configuration is never retained in a separate static.
    /// </remarks>
    public static ApplicationIdentitySnapshot Identity
    {
        get
        {
            var services = Current;
            if (services?.GetService(typeof(ApplicationIdentitySnapshot)) is ApplicationIdentitySnapshot identity)
            {
                return identity;
            }

            return global::Koan.Core.KoanEnv.CurrentSnapshot.Application;
        }
    }
}
