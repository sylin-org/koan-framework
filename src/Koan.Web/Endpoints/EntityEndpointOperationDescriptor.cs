namespace Koan.Web.Endpoints;

public sealed class EntityEndpointOperationDescriptor
{
    public required EntityEndpointOperationKind Kind { get; init; }

    public bool ReturnsCollection { get; init; }

    public bool RequiresBody { get; init; }

    public bool SupportsDatasetRouting { get; init; }

    public bool SupportsRelationships { get; init; }

    public bool SupportsShape { get; init; }

    public bool SupportsQueryFiltering { get; init; }
}
