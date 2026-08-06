using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.Routing;

internal enum DataRouteOrigin
{
    Default,
    ExplicitSource,
    DatabaseAxis,
    AmbientAdapter,
    EntityAttribute
}

/// <summary>One immutable operation/repository binding to a physical route generation.</summary>
internal sealed record DataRouteBinding(
    DataSourcePlan Plan,
    DataRouteOrigin Origin,
    long AuthorityRevision,
    long ContentGeneration)
{
    public bool IsDefaultDerived => Origin == DataRouteOrigin.Default;

    public string Namespace =>
        $"data-route:{Plan.RouteIdentity}:{ContentGeneration}";

    public string RepositoryIdentity => IsDefaultDerived
        ? $"{Namespace}:default:{AuthorityRevision}"
        : $"{Namespace}:explicit";
}
