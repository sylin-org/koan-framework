using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Data.Core;
using Koan.Mcp.Options;
using Koan.Mcp;
using Koan.Web.Endpoints;
using Microsoft.Extensions.Logging;
using static Koan.Mcp.Schema.SchemaExtensions;

namespace Koan.Mcp.Schema;

/// <summary>
/// Builds JSON Schema payloads for MCP tool definitions.
/// </summary>
public sealed class SchemaBuilder
{
    private static readonly MethodInfo AggregateGetOrAdd = typeof(AggregateBags)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(AggregateBags.GetOrAdd) && m.GetGenericArguments().Length == 3);

    private readonly IServiceProvider _services;
    private readonly ILogger<SchemaBuilder> _logger;

    public SchemaBuilder(IServiceProvider services, ILogger<SchemaBuilder> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public JsonObject BuildParametersSchema(
        Type entityType,
        Type keyType,
        EntityEndpointDescriptor descriptor,
        EntityEndpointOperationDescriptor operation,
        McpEntityAttribute attribute,
        McpEntityOverride? entityOverride)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        if (keyType is null) throw new ArgumentNullException(nameof(keyType));
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        if (attribute is null) throw new ArgumentNullException(nameof(attribute));

        var overrideJson = entityOverride?.SchemaOverride ?? attribute.SchemaOverride;
        if (!string.IsNullOrWhiteSpace(overrideJson))
        {
            try
            {
                var node = JsonNode.Parse(overrideJson);
                if (node is JsonObject obj)
                {
                    return obj;
                }

                _logger.LogWarning("Entity {Entity} supplied schema override that is not a JSON object. Falling back to automatic schema.", entityType.FullName);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse schema override for entity {Entity}. Falling back to automatic schema.", entityType.FullName);
            }
        }

        var bagKey = $"mcp:schema:{operation.Kind}";
        return GetOrBuild(entityType, keyType, bagKey, () => BuildSchemaInternal(entityType, keyType, descriptor, operation));
    }

    private JsonObject GetOrBuild(Type entityType, Type keyType, string bagKey, Func<JsonObject> factory)
    {
        var method = AggregateGetOrAdd.MakeGenericMethod(entityType, keyType, typeof(JsonObject));
        var result = method.Invoke(null, new object[] { _services, bagKey, factory });
        return (JsonObject)result!;
    }

    private JsonObject BuildSchemaInternal(Type entityType, Type keyType, EntityEndpointDescriptor descriptor, EntityEndpointOperationDescriptor operation)
    {
        return operation.Kind switch
        {
            EntityEndpointOperationKind.Collection => BuildCollectionSchema(descriptor, operation),
            EntityEndpointOperationKind.Query => BuildQuerySchema(descriptor, operation),
            EntityEndpointOperationKind.GetNew => BuildGetNewSchema(),
            EntityEndpointOperationKind.GetById => BuildGetByIdSchema(keyType, operation),
            EntityEndpointOperationKind.Upsert => BuildUpsertSchema(entityType, descriptor),
            EntityEndpointOperationKind.UpsertMany => BuildUpsertManySchema(entityType, descriptor),
            EntityEndpointOperationKind.Delete => BuildDeleteSchema(keyType, operation),
            EntityEndpointOperationKind.DeleteMany => BuildDeleteManySchema(keyType, operation),
            EntityEndpointOperationKind.DeleteByQuery => BuildDeleteByQuerySchema(operation),
            EntityEndpointOperationKind.DeleteAll => BuildDeleteAllSchema(operation),
            EntityEndpointOperationKind.Patch => BuildPatchSchema(entityType, keyType),
            _ => CreateObjectSchema()
        };
    }

    private JsonObject BuildCollectionSchema(EntityEndpointDescriptor descriptor, EntityEndpointOperationDescriptor operation)
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        var metadata = descriptor.Metadata;

        props["q"] = CreateStringProperty("Full-text query term.");
        props["filter"] = CreateStringProperty("JSON filter expression compiled into repository predicates.");
        props["ignoreCase"] = CreateBooleanProperty("When true string comparisons ignore case sensitivity.");
        props["page"] = new JsonObject
        {
            ["type"] = "integer",
            ["minimum"] = 1,
            ["description"] = "Page number to request."
        };
        props["pageSize"] = new JsonObject
        {
            ["type"] = "integer",
            ["minimum"] = 1,
            ["maximum"] = metadata.MaxPageSize,
            ["description"] = $"Number of items per page (default {metadata.DefaultPageSize})."
        };
        props["sort"] = CreateStringProperty("Sort expression using field[:direction] format.");
        props["accept"] = CreateStringProperty("Optional Accept header override (view negotiation).");
        props["forcePagination"] = CreateBooleanProperty("When true forces pagination even when repository returns a full set.");
        props["extras"] = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Additional query parameters forwarded to hooks.",
            ["additionalProperties"] = new JsonObject { ["type"] = "string" }
        };

        if (operation.SupportsDatasetRouting)
        {
            props["set"] = CreateStringProperty("Dataset key when using multitenant routing.");
        }

        if (operation.SupportsShape && metadata.AllowedShapes.Count > 0)
        {
            props["shape"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Response shaping hint.",
                ["enum"] = new JsonArray(metadata.AllowedShapes.Select(s => (JsonNode)s).ToArray())
            };
        }

        if (operation.SupportsRelationships)
        {
            props["with"] = CreateStringProperty("Relationship expansion hints (e.g. with=all).");
        }

        return schema;
    }

    private JsonObject BuildQuerySchema(EntityEndpointDescriptor descriptor, EntityEndpointOperationDescriptor operation)
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["filter"] = CreateStringProperty("JSON filter expression used to construct the query.");
        props["ignoreCase"] = CreateBooleanProperty("When true string comparisons ignore case sensitivity.");
        props["accept"] = CreateStringProperty("Optional Accept header override (view negotiation).");
        if (operation.SupportsDatasetRouting)
        {
            props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        }
        return schema;
    }

    private static JsonObject BuildGetNewSchema()
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["accept"] = CreateStringProperty("Optional Accept header override (view negotiation).");
        return schema;
    }

    private JsonObject BuildGetByIdSchema(Type keyType, EntityEndpointOperationDescriptor operation)
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["id"] = CreateSimpleTypeSchema(keyType, "Entity identifier used to load the model.");
        if (operation.SupportsDatasetRouting)
        {
            props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        }
        if (operation.SupportsRelationships)
        {
            props["with"] = CreateStringProperty("Relationship expansion hints (e.g. with=all).");
        }
        props["accept"] = CreateStringProperty("Optional Accept header override (view negotiation).");
        schema["required"] = new JsonArray { "id" };
        return schema;
    }

    private JsonObject BuildUpsertSchema(Type entityType, EntityEndpointDescriptor descriptor)
    {
        _ = descriptor;
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["model"] = BuildEntitySchema(entityType, "Entity payload to insert or update.", EntityEndpointOperationKind.Upsert);
        props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        props["accept"] = CreateStringProperty("Optional Accept header override (view negotiation).");
        schema["required"] = new JsonArray { "model" };
        return schema;
    }

    private JsonObject BuildUpsertManySchema(Type entityType, EntityEndpointDescriptor descriptor)
    {
        _ = descriptor;
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["models"] = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "Collection of entity payloads to insert or update.",
            ["items"] = BuildEntitySchema(entityType, operation: EntityEndpointOperationKind.UpsertMany)
        };
        props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        schema["required"] = new JsonArray { "models" };
        return schema;
    }

    private JsonObject BuildDeleteSchema(Type keyType, EntityEndpointOperationDescriptor operation)
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["id"] = CreateSimpleTypeSchema(keyType, "Entity identifier targeted for deletion.");
        schema["required"] = new JsonArray { "id" };
        if (operation.SupportsDatasetRouting)
        {
            props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        }
        return schema;
    }

    private JsonObject BuildDeleteManySchema(Type keyType, EntityEndpointOperationDescriptor operation)
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["ids"] = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "Identifiers targeted for deletion.",
            ["items"] = CreateSimpleTypeSchema(keyType, null)
        };
        schema["required"] = new JsonArray { "ids" };
        if (operation.SupportsDatasetRouting)
        {
            props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        }
        return schema;
    }

    private JsonObject BuildDeleteByQuerySchema(EntityEndpointOperationDescriptor operation)
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["query"] = CreateStringProperty("Query expression used to select records for deletion.");
        schema["required"] = new JsonArray { "query" };
        if (operation.SupportsDatasetRouting)
        {
            props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        }
        return schema;
    }

    private JsonObject BuildDeleteAllSchema(EntityEndpointOperationDescriptor operation)
    {
        var schema = CreateObjectSchema();
        if (operation.SupportsDatasetRouting)
        {
            var props = (JsonObject)schema["properties"]!;
            props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        }
        return schema;
    }

    private JsonObject BuildPatchSchema(Type entityType, Type keyType)
    {
        var schema = CreateObjectSchema();
        var props = (JsonObject)schema["properties"]!;
        props["id"] = CreateSimpleTypeSchema(keyType, "Entity identifier targeted for patching.");
        props["patch"] = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "JSON Patch operations applied to the entity.",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray { "op", "path" },
                ["properties"] = new JsonObject
                {
                    ["op"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "add", "remove", "replace", "move", "copy", "test" }
                    },
                    ["path"] = CreateStringProperty("JSON Pointer path to the target field."),
                    ["value"] = new JsonObject
                    {
                        ["description"] = "Optional value depending on operation type."
                    },
                    ["from"] = CreateStringProperty("Source path for move/copy operations.")
                },
                ["additionalProperties"] = false
            }
        };
        props["set"] = CreateStringProperty("Dataset key when routing to a specific tenant or dataset.");
        props["accept"] = CreateStringProperty("Optional Accept header override (view negotiation).");
        schema["required"] = new JsonArray { "id", "patch" };
        return schema;
    }

    private JsonObject BuildEntitySchema(Type entityType, string? description = null, EntityEndpointOperationKind? operation = null)
    {
        var schema = CreateObjectSchema().WithDescription(description);
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetGetMethod() is null) continue;
            var propSchema = CreateSchemaForProperty(property, operation);
            if (propSchema is null) continue;
            props[property.Name] = propSchema;
            if (IsRequired(property))
            {
                required.Add(property.Name);
            }
        }

        schema["properties"] = props;
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private bool IsRequired(PropertyInfo property)
    {
        var requiredAttribute = property.GetCustomAttribute<RequiredAttribute>();
        return requiredAttribute is not null && !requiredAttribute.AllowEmptyStrings;
    }

    private JsonObject? CreateSchemaForProperty(PropertyInfo property, EntityEndpointOperationKind? operation)
    {
        var schema = CreateSimpleTypeSchema(property.PropertyType, GetPropertyDescription(property, operation));
        if (schema is null)
        {
            _logger.LogDebug("Skipping property {Property} on {Entity} because it cannot be translated to JSON schema.", property.Name, property.DeclaringType?.FullName);
        }
        return schema;
    }

    private string? GetPropertyDescription(PropertyInfo property, EntityEndpointOperationKind? operation)
    {
        foreach (var attribute in property.GetCustomAttributes<McpDescriptionAttribute>())
        {
            if (attribute.Operation == EntityEndpointOperationKind.None)
            {
                return attribute.Description;
            }

            if (operation.HasValue && attribute.Operation == operation.Value)
            {
                return attribute.Description;
            }
        }

        var display = property.GetCustomAttribute<DisplayAttribute>();
        if (!string.IsNullOrWhiteSpace(display?.Description))
        {
            return display.Description;
        }

        var description = property.GetCustomAttribute<DescriptionAttribute>();
        if (!string.IsNullOrWhiteSpace(description?.Description))
        {
            return description.Description;
        }

        _logger.LogWarning("Property {Property} on {Entity} is missing description metadata. Using property name as fallback.", property.Name, property.DeclaringType?.FullName);
        return property.Name;
    }

    private JsonObject? CreateSimpleTypeSchema(Type type, string? description)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        JsonObject? schema = null;
        if (type == typeof(string) || type == typeof(char))
        {
            schema = new JsonObject { ["type"] = "string" };
        }
        else if (type == typeof(bool))
        {
            schema = new JsonObject { ["type"] = "boolean" };
        }
        else if (type.IsEnum)
        {
            schema = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(type.GetEnumNames().Select(n => (JsonNode)n).ToArray())
            };
        }
        else if (type == typeof(Guid))
        {
            schema = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid"
            };
        }
        else if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            schema = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time"
            };
        }
        else if (type == typeof(TimeSpan))
        {
            schema = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "duration"
            };
        }
        else if (IsInteger(type))
        {
            schema = new JsonObject { ["type"] = "integer" };
        }
        else if (IsNumber(type))
        {
            schema = new JsonObject { ["type"] = "number" };
        }
        else if (TryGetEnumerableElementType(type, out var elementType))
        {
            schema = new JsonObject
            {
                ["type"] = "array",
                ["items"] = CreateSimpleTypeSchema(elementType, null) ?? new JsonObject()
            };
        }
        else if (type.IsClass && type != typeof(object))
        {
            schema = BuildEntitySchema(type);
        }

        if (schema is not null && !string.IsNullOrWhiteSpace(description))
        {
            schema["description"] = description;
        }

        return schema;
    }

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType() ?? typeof(object);
            return true;
        }

        if (type.IsGenericType)
        {
            if (type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = iface.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = typeof(object);
        return false;
    }

    private static bool IsInteger(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
    }

    private static bool IsNumber(Type type)
    {
        return type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

}

