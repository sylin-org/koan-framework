using System;
using System.Collections.Generic;
using Koan.Data.Core.Relationships;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Resources;

/// <summary>
/// AN7 (docs/assessment/09 §5) — projects an entity's declared relationships (<c>[Parent]</c>/<c>[ParentOf]</c>,
/// read via <see cref="IRelationshipMetadata"/>) as terse navigable EDGES for the <c>koan://entities</c>
/// catalog. An edge is a route, never a verb — the whole graph is navigable without any edge becoming a tool
/// (no catalog explosion). Each edge carries the navigation recipe: the <c>target</c> entity tool to query
/// and the <c>via</c> field to filter on (the field name gives the mechanism). The actual traversal runs
/// through the already-governed Query tool, so it inherits that resolved query's per-grant projection.
///
/// Per-grant at the catalog level: an edge whose TARGET type is not visible to this grant is ABSENT
/// (walled-means-silent — the edge's field name would otherwise leak the walled target's existence).
/// </summary>
internal static class EntityEdgeProjection
{
    /// <summary>The navigable edges of <paramref name="entityType"/>, restricted to targets in
    /// <paramref name="visibleByType"/> (the grant-visible MCP entity types → their display names).</summary>
    public static JArray For(
        Type entityType,
        IRelationshipMetadata metadata,
        IReadOnlyDictionary<Type, string> visibleByType)
    {
        var edges = new JArray();

        // Parent edges (to-one): a [Parent] foreign key on this entity resolves to the parent type.
        foreach (var (propertyName, parentType) in metadata.GetParentRelationships(entityType))
        {
            if (!visibleByType.TryGetValue(parentType, out var target)) continue; // walled target → silent
            edges.Add(Edge(name: propertyName, kind: "parent", target: target, via: propertyName, cardinality: "one"));
        }

        // Child edges (to-many): entities elsewhere declare a [Parent] foreign key pointing back at this type.
        foreach (var (referenceProperty, childType) in metadata.GetChildRelationships(entityType))
        {
            if (!visibleByType.TryGetValue(childType, out var target)) continue; // walled target → silent
            edges.Add(Edge(name: $"{target}.{referenceProperty}", kind: "child", target: target, via: referenceProperty, cardinality: "many"));
        }

        return edges;
    }

    private static JObject Edge(string name, string kind, string target, string via, string cardinality)
        => new JObject
        {
            ["name"] = name,
            ["kind"] = kind,
            ["target"] = target,
            ["via"] = via,
            ["cardinality"] = cardinality
        };
}
