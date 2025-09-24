using System.Collections.Generic;

namespace Koan.Web.Endpoints;

public sealed class EntityEndpointDescriptorMetadata
{
    public required int DefaultPageSize { get; init; }

    public required int MaxPageSize { get; init; }

    public required string DefaultView { get; init; }

    public required IReadOnlyList<string> AllowedShapes { get; init; }

    public required bool AllowRelationshipExpansion { get; init; }
}
