using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Koan.Web.Endpoints;

namespace Koan.Mcp;

public sealed record McpToolDefinition(
    string Name,
    string EntityName,
    EntityEndpointOperationKind Operation,
    JObject InputSchema,
    bool ReturnsCollection,
    bool IsMutation,
    string? Description,
    IReadOnlyList<string> RequiredScopes
);
