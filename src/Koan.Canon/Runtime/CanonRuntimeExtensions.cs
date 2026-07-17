using Microsoft.Extensions.DependencyInjection;
using Koan.Canon;

using Koan.Core.Hosting.App;

namespace Koan.Canon;

/// <summary>
/// Convenience extensions for interacting with the canon runtime.
/// </summary>
public static class CanonRuntimeExtensions
{
    /// <summary>
    /// Canonizes the entity using a runtime resolved from, and flow-scoped to, the provided service provider.
    /// </summary>
    public static async Task<CanonizationResult<T>> Canonize<T>(this T entity, IServiceProvider services, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new()
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        using var hostScope = AppHost.PushScope(services);
        var runtime = services.GetRequiredService<ICanonRuntime>();
        return await runtime.Canonize(entity, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Canonizes the entity using the provided runtime instance.
    /// </summary>
    /// <remarks>
    /// The caller owns host selection for this overload. A runtime using default persistence requires
    /// the intended Koan provider to be active for the complete operation.
    /// </remarks>
    public static Task<CanonizationResult<T>> Canonize<T>(this T entity, ICanonRuntime runtime, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new()
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (runtime is null)
        {
            throw new ArgumentNullException(nameof(runtime));
        }

        return runtime.Canonize(entity, options, cancellationToken);
    }

    /// <summary>
    /// Rebuilds views using a runtime resolved from, and flow-scoped to, the provided service provider.
    /// </summary>
    public static async Task RebuildViews<T>(this T entity, IServiceProvider services, string[]? views = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new()
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        using var hostScope = AppHost.PushScope(services);
        var runtime = services.GetRequiredService<ICanonRuntime>();
        await runtime.RebuildViews<T>(entity.Id, views, cancellationToken).ConfigureAwait(false);
    }
}
