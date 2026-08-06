using System.ComponentModel;

namespace Koan.Data.Core.Decorators;

/// <summary>Compatible decorator extension for concerns whose identity must include the bound physical Data route.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IDataRouteAwareRepositoryDecorator : IDataRepositoryDecorator
{
    object? TryDecorate(
        Type entityType,
        Type keyType,
        object repository,
        DataRepositoryDecorationContext context,
        IServiceProvider services);
}
