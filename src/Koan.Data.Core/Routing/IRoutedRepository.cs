namespace Koan.Data.Core.Routing;

/// <summary>A repository's physical route, which a hold on its entity's own route opens on (<see cref="Data{TEntity,TKey}.Hold"/>).</summary>
internal interface IRoutedRepository
{
    DataRouteBinding? RouteBinding { get; }
}
