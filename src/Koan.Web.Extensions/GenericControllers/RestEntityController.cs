using Koan.Data.Abstractions;
using Koan.Web.Controllers;

namespace Koan.Web.Extensions.GenericControllers;

/// <summary>
/// ARCH-0092 (§B): the concrete full-CRUD controller that the terse <c>[RestEntity]</c> path materializes.
/// <see cref="EntityController{TEntity,TKey}"/> is <c>abstract</c> (it's authored by subclassing), so the
/// terse path needs a concrete closure for MVC to instantiate. This class adds no behavior of its own — it
/// inherits the entire governed <c>IEntityEndpointService</c> surface; its route is applied by
/// <see cref="GenericControllers.RouteConvention"/> from the registration entry.
/// </summary>
public sealed class RestEntityController<TEntity, TKey> : EntityController<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
}
