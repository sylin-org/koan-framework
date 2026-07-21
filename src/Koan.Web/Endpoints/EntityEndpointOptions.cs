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

    /// <summary>Maximum children materialized for one relationship edge across a request.</summary>
    public int RelationshipMaxResults { get; set; } = KoanWebConstants.Defaults.MaxPageSize;

    /// <summary>
    /// Optional candidate bound for scan-backed relationship expansion. Null is the safe default:
    /// scan providers are rejected until the application explicitly chooses a finite budget.
    /// </summary>
    public int? RelationshipFallbackMaxCandidates { get; set; }

    /// <summary>
    /// When true, unresolvable sort fields are silently skipped (with a <c>Koan-Sort-Skipped</c> response header)
    /// instead of producing a 400 Bad Request. Default false (strict) — see DATA-0092.
    /// </summary>
    public bool LenientSort { get; set; } = false;

    public bool IsShapeAllowed(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape)) return false;
        if (AllowedShapes is null || AllowedShapes.Count == 0) return false;
        return AllowedShapes.Any(s => string.Equals(s, shape, StringComparison.OrdinalIgnoreCase));
    }
}
