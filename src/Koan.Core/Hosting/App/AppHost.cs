namespace Koan.Core.Hosting.App;

/// <summary>
/// Ambient service provider holder for terse APIs and cross-cutting helpers.
/// </summary>
/// <remarks>
/// <para>
/// Production code calls <c>AppHost.Current = sp</c> once during startup; the value is held in
/// a static field and is visible to every consumer that doesn't enter an explicit scope.
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
    private static ApplicationIdentitySnapshot _identity = ApplicationIdentitySnapshot.Empty;

    public static IServiceProvider? Current
    {
        get => _scoped.Value ?? _global;
        set => _global = value;
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

    public static ApplicationIdentitySnapshot Identity => _identity;

    internal static void SetIdentity(ApplicationIdentitySnapshot snapshot)
    {
        _identity = snapshot;
    }
}
