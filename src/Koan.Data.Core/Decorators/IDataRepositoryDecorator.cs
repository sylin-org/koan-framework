using System;

namespace Koan.Data.Core.Decorators;

/// <summary>
/// Provides extension points for decorating data repositories before they are cached and returned
/// by <see cref="DataService"/>.
/// </summary>
public interface IDataRepositoryDecorator
{
    /// <summary>
    /// Attempts to decorate the provided repository instance.
    /// </summary>
    /// <param name="entityType">The aggregate type associated with the repository.</param>
    /// <param name="keyType">The key type for the aggregate.</param>
    /// <param name="repository">The repository instance to decorate.</param>
    /// <param name="services">The current service provider.</param>
    /// <returns>The decorated repository instance, or <c>null</c> when no decoration was applied.</returns>
    object? TryDecorate(Type entityType, Type keyType, object repository, IServiceProvider services);
}