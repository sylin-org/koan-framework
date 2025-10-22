using Microsoft.Extensions.DependencyInjection;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Convenience extensions for interacting with the canon runtime.
/// </summary>
public static class CanonRuntimeExtensions
{
    /// <summary>
    /// Canonizes the entity using a runtime resolved from the provided service provider.
    /// </summary>
    public static Task<CanonizationResult<T>> Canonize<T>(this T entity, IServiceProvider services, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
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

        var runtime = services.GetRequiredService<ICanonRuntime>();
        return runtime.Canonize(entity, options, cancellationToken);
    }

    /// <summary>
    /// Canonizes the entity using the provided runtime instance.
    /// </summary>
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
    /// Rebuilds views for the entity using a runtime resolved from the provider.
    /// </summary>
    public static Task RebuildViews<T>(this T entity, IServiceProvider services, string[]? views = null, CancellationToken cancellationToken = default)
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

        var runtime = services.GetRequiredService<ICanonRuntime>();
        return runtime.RebuildViews<T>(entity.Id, views, cancellationToken);
    }
}
