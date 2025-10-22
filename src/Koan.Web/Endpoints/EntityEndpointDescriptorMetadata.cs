using System.Collections.Generic;

namespace Koan.Web.Endpoints;

public sealed class EntityEndpointDescriptorMetadata
{
    public required int DefaultPageSize { get; init; }

    public required int MaxPageSize { get; init; }

    public required string DefaultView { get; init; }

    public required IReadOnlyList<string> AllowedShapes { get; init; }

    public required bool AllowRelationshipExpansion { get; init; }

    /// <summary>
    /// Optional list of dataset/set identifiers supported by this entity (multitenant or logical partitions).
    /// When null or empty no set union type will be generated.
    /// </summary>
    public IReadOnlyList<string>? AvailableSets { get; init; }

    /// <summary>
    /// Optional list of relationship expansion tokens (e.g. assignedUser, tags). "all" is implied.
    /// When null or empty only generic string will be used in types.
    /// </summary>
    public IReadOnlyList<string>? RelationshipNames { get; init; }
}
