using Koan.Web.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Web.Endpoints;

public sealed class EntityEndpointOptions
{
    public int DefaultPageSize { get; set; } = KoanWebConstants.Defaults.DefaultPageSize;

    public int MaxPageSize { get; set; } = KoanWebConstants.Defaults.MaxPageSize;

    public string DefaultView { get; set; } = "full";

    public List<string> AllowedShapes { get; set; } = new() { "map", "dict" };

    public bool AllowRelationshipExpansion { get; set; } = true;

    public bool IsShapeAllowed(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape)) return false;
        if (AllowedShapes is null || AllowedShapes.Count == 0) return false;
        return AllowedShapes.Any(s => string.Equals(s, shape, StringComparison.OrdinalIgnoreCase));
    }
}
