using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Composition;

/// <summary>
/// Identifies the exact application composition currently being declared.
/// </summary>
/// <remarks>
/// <para>
/// Koan's terse static capability facets need an owner while the host is still being composed.
/// This flow-local scope supplies that owner without retaining a process-global service collection.
/// Registrations therefore belong to the host being built and cannot leak into a later host.
/// </para>
/// <para>
/// Application code normally enters this scope through <c>AddKoan(() =&gt; ...)</c>. Framework
/// modules enter it automatically while their registration methods run.
/// </para>
/// </remarks>
public static class KoanCompositionScope
{
    private static readonly AsyncLocal<IServiceCollection?> Current = new();

    /// <summary>
    /// Gets the service collection that owns the current declaration.
    /// </summary>
    /// <exception cref="InvalidOperationException">No Koan composition is active.</exception>
    public static IServiceCollection RequireServices(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return Current.Value ?? throw new InvalidOperationException(
            $"{operation} must be declared while Koan is composing the application. " +
            "Place the declaration inside builder.Services.AddKoan(() => { ... }) or a Koan module registration method.");
    }

    internal static IDisposable Enter(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var previous = Current.Value;
        Current.Value = services;
        return new Pop(previous);
    }

    private sealed class Pop(IServiceCollection? previous) : IDisposable
    {
        private IServiceCollection? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Current.Value = Interlocked.Exchange(ref _previous, null);
        }
    }
}
