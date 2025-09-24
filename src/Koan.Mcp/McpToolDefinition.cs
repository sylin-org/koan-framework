using System.Collections.Generic;
using System.Text.Json.Nodes;
using Koan.Web.Endpoints;

namespace Koan.Mcp;

public sealed record McpToolDefinition(
    string Name,
    string EntityName,
    EntityEndpointOperationKind Operation,
    JsonObject InputSchema,
    bool ReturnsCollection,
    bool IsMutation,
    string? Description,
    IReadOnlyList<string> RequiredScopes
);
