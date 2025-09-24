using System;
using System.Collections.Generic;
using Koan.Mcp.Options;
using Koan.Mcp.Schema;
using Koan.Web.Endpoints;
using Microsoft.Extensions.Logging;

namespace Koan.Mcp;

/// <summary>
/// Converts entity descriptors into MCP tool definitions.
/// </summary>
public sealed class DescriptorMapper
{
    private static readonly HashSet<EntityEndpointOperationKind> MutationKinds = new()
    {
        EntityEndpointOperationKind.Upsert,
        EntityEndpointOperationKind.UpsertMany,
        EntityEndpointOperationKind.Delete,
        EntityEndpointOperationKind.DeleteMany,
        EntityEndpointOperationKind.DeleteByQuery,
        EntityEndpointOperationKind.DeleteAll,
        EntityEndpointOperationKind.Patch
    };

    private readonly SchemaBuilder _schemaBuilder;
    private readonly ILogger<DescriptorMapper> _logger;

    public DescriptorMapper(SchemaBuilder schemaBuilder, ILogger<DescriptorMapper> logger)
    {
        _schemaBuilder = schemaBuilder ?? throw new ArgumentNullException(nameof(schemaBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<McpToolDefinition> Map(
        Type entityType,
        Type keyType,
        EntityEndpointDescriptor descriptor,
        McpEntityAttribute attribute,
        McpEntityOverride? entityOverride,
        string displayName)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        if (keyType is null) throw new ArgumentNullException(nameof(keyType));
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        if (attribute is null) throw new ArgumentNullException(nameof(attribute));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));

        var allowMutations = entityOverride?.AllowMutations ?? attribute.AllowMutations;
        var requiredScopes = entityOverride?.RequiredScopes?.Length > 0
            ? entityOverride!.RequiredScopes
            : attribute.RequiredScopes;
        var tools = new List<McpToolDefinition>();

        foreach (var operation in descriptor.Operations)
        {
            var isMutation = MutationKinds.Contains(operation.Kind);
            if (isMutation && !allowMutations)
            {
                _logger.LogDebug("Skipping mutation operation {Operation} for entity {Entity} because mutations are disabled.", operation.Kind, entityType.FullName);
                continue;
            }

            var toolName = BuildToolName(displayName, attribute.ToolPrefix, operation.Kind);
            var jsonSchema = _schemaBuilder.BuildParametersSchema(entityType, keyType, descriptor, operation, attribute, entityOverride);
            var description = BuildDescription(attribute.Description, operation.Kind, displayName);

            tools.Add(new McpToolDefinition(
                toolName,
                displayName,
                operation.Kind,
                jsonSchema,
                operation.ReturnsCollection,
                isMutation,
                description,
                requiredScopes
            ));
        }

        return tools;
    }

    private static string BuildToolName(string displayName, string? prefix, EntityEndpointOperationKind operation)
    {
        var entitySegment = ToKebabCase(displayName);
        var operationSegment = operation switch
        {
            EntityEndpointOperationKind.Collection => "collection",
            EntityEndpointOperationKind.Query => "query",
            EntityEndpointOperationKind.GetNew => "new",
            EntityEndpointOperationKind.GetById => "get-by-id",
            EntityEndpointOperationKind.Upsert => "upsert",
            EntityEndpointOperationKind.UpsertMany => "upsert-many",
            EntityEndpointOperationKind.Delete => "delete",
            EntityEndpointOperationKind.DeleteMany => "delete-many",
            EntityEndpointOperationKind.DeleteByQuery => "delete-by-query",
            EntityEndpointOperationKind.DeleteAll => "delete-all",
            EntityEndpointOperationKind.Patch => "patch",
            _ => operation.ToString().ToLowerInvariant()
        };

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return $"{entitySegment}.{operationSegment}";
        }

        return $"{ToKebabCase(prefix)}.{entitySegment}.{operationSegment}";
    }

    private static string BuildDescription(string? entityDescription, EntityEndpointOperationKind operation, string displayName)
    {
        var focus = string.IsNullOrWhiteSpace(entityDescription) ? displayName : entityDescription;

        return operation switch
        {
            EntityEndpointOperationKind.Collection => $"List {focus} records with paging, filtering, and shaping.",
            EntityEndpointOperationKind.Query => $"Run an advanced query against {focus} records.",
            EntityEndpointOperationKind.GetNew => $"Fetch server defaults for a new {focus} instance.",
            EntityEndpointOperationKind.GetById => $"Retrieve a {focus} by identifier.",
            EntityEndpointOperationKind.Upsert => $"Insert or update a {focus} record.",
            EntityEndpointOperationKind.UpsertMany => $"Insert or update many {focus} records.",
            EntityEndpointOperationKind.Delete => $"Delete a {focus} by identifier.",
            EntityEndpointOperationKind.DeleteMany => $"Delete multiple {focus} records by identifier.",
            EntityEndpointOperationKind.DeleteByQuery => $"Delete {focus} records matching a query expression.",
            EntityEndpointOperationKind.DeleteAll => $"Delete all {focus} records.",
            EntityEndpointOperationKind.Patch => $"Apply a JSON Patch document to a {focus} record.",
            _ => $"Execute {operation} for {focus}."
        };
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var chars = new List<char>(value.Length);
        foreach (var c in value)
        {
            if (char.IsUpper(c) && chars.Count > 0 && chars[^1] != '-')
            {
                chars.Add('-');
            }
            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }
}
