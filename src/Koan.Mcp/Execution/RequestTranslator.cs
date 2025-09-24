using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using JsonException = System.Text.Json.JsonException;

namespace Koan.Mcp.Execution;

public sealed class RequestTranslator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public RequestTranslation Translate(
        IServiceProvider services,
        McpEntityRegistration registration,
        McpToolDefinition tool,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (registration is null) throw new ArgumentNullException(nameof(registration));
        if (tool is null) throw new ArgumentNullException(nameof(tool));

        var args = arguments ?? new JsonObject();
        var builder = services.GetRequiredService<EntityRequestContextBuilder>();
        var context = BuildContext(builder, args, cancellationToken);

        return tool.Operation switch
        {
            EntityEndpointOperationKind.Collection => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.GetCollectionAsync),
                BuildCollectionRequest(context, args)),
            EntityEndpointOperationKind.Query => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.QueryAsync),
                BuildQueryRequest(context, args)),
            EntityEndpointOperationKind.GetNew => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.GetNewAsync),
                new EntityGetNewRequest
                {
                    Context = context,
                    Accept = ReadString(args, "accept")
                }),
            EntityEndpointOperationKind.GetById => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.GetByIdAsync),
                BuildGetByIdRequest(registration, context, args)),
            EntityEndpointOperationKind.Upsert => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.UpsertAsync),
                BuildUpsertRequest(registration, context, args)),
            EntityEndpointOperationKind.UpsertMany => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.UpsertManyAsync),
                BuildUpsertManyRequest(registration, context, args)),
            EntityEndpointOperationKind.Delete => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.DeleteAsync),
                BuildDeleteRequest(registration, context, args)),
            EntityEndpointOperationKind.DeleteMany => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.DeleteManyAsync),
                BuildDeleteManyRequest(registration, context, args)),
            EntityEndpointOperationKind.DeleteByQuery => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.DeleteByQueryAsync),
                BuildDeleteByQueryRequest(context, args)),
            EntityEndpointOperationKind.DeleteAll => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.DeleteAllAsync),
                new EntityDeleteAllRequest
                {
                    Context = context,
                    Set = ReadString(args, "set")
                }),
            EntityEndpointOperationKind.Patch => new RequestTranslation(
                nameof(IEntityEndpointService<object, object>.PatchAsync),
                BuildPatchRequest(registration, context, args)),
            _ => throw new JsonException($"Operation '{tool.Operation}' is not supported.")
        };
    }

    private static EntityCollectionRequest BuildCollectionRequest(EntityRequestContext context, JsonObject args)
    {
        return new EntityCollectionRequest
        {
            Context = context,
            FilterJson = ReadString(args, "filter"),
            Set = ReadString(args, "set"),
            IgnoreCase = ReadBool(args, "ignoreCase") ?? false,
            With = ReadString(args, "with"),
            Shape = ReadString(args, "shape"),
            ForcePagination = ReadBool(args, "forcePagination") ?? false,
            Accept = ReadString(args, "accept"),
            BasePath = ReadString(args, "basePath"),
            QueryParameters = BuildQueryParameters(args)
        };
    }

    private static EntityQueryRequest BuildQueryRequest(EntityRequestContext context, JsonObject args)
    {
        return new EntityQueryRequest
        {
            Context = context,
            FilterJson = ReadString(args, "filter"),
            Set = ReadString(args, "set"),
            IgnoreCase = ReadBool(args, "ignoreCase") ?? false,
            Accept = ReadString(args, "accept")
        };
    }

    private object BuildGetByIdRequest(McpEntityRegistration registration, EntityRequestContext context, JsonObject args)
    {
        var requestType = typeof(EntityGetByIdRequest<>).MakeGenericType(registration.KeyType);
        var request = Activator.CreateInstance(requestType)!;
        SetProperty(request, nameof(EntityGetByIdRequest<object>.Context), context);
        var idNode = TryGet(args, "id") ?? throw new JsonException("Missing required 'id' parameter.");
        SetProperty(request, nameof(EntityGetByIdRequest<object>.Id), ConvertValue(idNode, registration.KeyType));
        SetProperty(request, nameof(EntityGetByIdRequest<object>.Set), ReadString(args, "set"));
        SetProperty(request, nameof(EntityGetByIdRequest<object>.With), ReadString(args, "with"));
        SetProperty(request, nameof(EntityGetByIdRequest<object>.Accept), ReadString(args, "accept"));
        return request;
    }

    private object BuildUpsertRequest(McpEntityRegistration registration, EntityRequestContext context, JsonObject args)
    {
        var requestType = typeof(EntityUpsertRequest<>).MakeGenericType(registration.EntityType);
        var request = Activator.CreateInstance(requestType)!;
        SetProperty(request, nameof(EntityUpsertRequest<object>.Context), context);
        var modelNode = TryGet(args, "model") ?? throw new JsonException("Missing required 'model' payload.");
        SetProperty(request, nameof(EntityUpsertRequest<object>.Model), ConvertEntity(modelNode, registration.EntityType));
        SetProperty(request, nameof(EntityUpsertRequest<object>.Set), ReadString(args, "set"));
        SetProperty(request, nameof(EntityUpsertRequest<object>.Accept), ReadString(args, "accept"));
        return request;
    }

    private object BuildUpsertManyRequest(McpEntityRegistration registration, EntityRequestContext context, JsonObject args)
    {
        var requestType = typeof(EntityUpsertManyRequest<>).MakeGenericType(registration.EntityType);
        var request = Activator.CreateInstance(requestType)!;
        SetProperty(request, nameof(EntityUpsertManyRequest<object>.Context), context);
        var modelsNode = TryGet(args, "models") ?? throw new JsonException("Missing required 'models' payload.");
        SetProperty(request, nameof(EntityUpsertManyRequest<object>.Models), ConvertEntityCollection(modelsNode, registration.EntityType));
        SetProperty(request, nameof(EntityUpsertManyRequest<object>.Set), ReadString(args, "set"));
        return request;
    }

    private object BuildDeleteRequest(McpEntityRegistration registration, EntityRequestContext context, JsonObject args)
    {
        var requestType = typeof(EntityDeleteRequest<>).MakeGenericType(registration.KeyType);
        var request = Activator.CreateInstance(requestType)!;
        SetProperty(request, nameof(EntityDeleteRequest<object>.Context), context);
        var idNode = TryGet(args, "id") ?? throw new JsonException("Missing required 'id' parameter.");
        SetProperty(request, nameof(EntityDeleteRequest<object>.Id), ConvertValue(idNode, registration.KeyType));
        SetProperty(request, nameof(EntityDeleteRequest<object>.Set), ReadString(args, "set"));
        SetProperty(request, nameof(EntityDeleteRequest<object>.Accept), ReadString(args, "accept"));
        return request;
    }

    private object BuildDeleteManyRequest(McpEntityRegistration registration, EntityRequestContext context, JsonObject args)
    {
        var requestType = typeof(EntityDeleteManyRequest<>).MakeGenericType(registration.KeyType);
        var request = Activator.CreateInstance(requestType)!;
        SetProperty(request, nameof(EntityDeleteManyRequest<object>.Context), context);
        var idsNode = TryGet(args, "ids") ?? throw new JsonException("Missing required 'ids' collection.");
        SetProperty(request, nameof(EntityDeleteManyRequest<object>.Ids), ConvertKeyCollection(idsNode, registration.KeyType));
        SetProperty(request, nameof(EntityDeleteManyRequest<object>.Set), ReadString(args, "set"));
        return request;
    }

    private static EntityDeleteByQueryRequest BuildDeleteByQueryRequest(EntityRequestContext context, JsonObject args)
    {
        var query = ReadString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new JsonException("Missing required 'query' parameter.");
        }

        return new EntityDeleteByQueryRequest
        {
            Context = context,
            Query = query!,
            Set = ReadString(args, "set")
        };
    }

    private object BuildPatchRequest(McpEntityRegistration registration, EntityRequestContext context, JsonObject args)
    {
        var requestType = typeof(EntityPatchRequest<,>).MakeGenericType(registration.EntityType, registration.KeyType);
        var request = Activator.CreateInstance(requestType)!;
        SetProperty(request, nameof(EntityPatchRequest<object, object>.Context), context);
        var idNode = TryGet(args, "id") ?? throw new JsonException("Missing required 'id' parameter.");
        SetProperty(request, nameof(EntityPatchRequest<object, object>.Id), ConvertValue(idNode, registration.KeyType));
        var patchNode = TryGet(args, "patch") ?? throw new JsonException("Missing required 'patch' payload.");
        SetProperty(request, nameof(EntityPatchRequest<object, object>.Patch), ConvertPatchDocument(patchNode, registration.EntityType));
        SetProperty(request, nameof(EntityPatchRequest<object, object>.Set), ReadString(args, "set"));
        SetProperty(request, nameof(EntityPatchRequest<object, object>.Accept), ReadString(args, "accept"));
        return request;
    }

    private static EntityRequestContext BuildContext(EntityRequestContextBuilder builder, JsonObject args, CancellationToken cancellationToken)
    {
        var options = new QueryOptions();

        var q = ReadString(args, "q");
        if (!string.IsNullOrWhiteSpace(q)) options.Q = q;

        var page = ReadInt(args, "page");
        if (page.HasValue && page.Value > 0) options.Page = page.Value;

        var pageSize = ReadInt(args, "pageSize");
        if (pageSize.HasValue && pageSize.Value > 0) options.PageSize = pageSize.Value;

        var shape = ReadString(args, "shape");
        if (!string.IsNullOrWhiteSpace(shape)) options.Shape = shape!;

        var view = ReadString(args, "view");
        if (!string.IsNullOrWhiteSpace(view)) options.View = view;

        var sort = ReadString(args, "sort");
        if (!string.IsNullOrWhiteSpace(sort))
        {
            options.Sort.Add(ParseSort(sort!));
        }

        if (args.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extrasObj)
        {
            foreach (var kv in extrasObj)
            {
                if (kv.Value is JsonValue value && value.TryGetValue(out string? stringValue))
                {
                    options.Extras[kv.Key] = stringValue ?? string.Empty;
                }
                else if (kv.Value is not null)
                {
                    options.Extras[kv.Key] = kv.Value.ToJsonString();
                }
            }
        }

        return builder.Build(options, cancellationToken);
    }

    private static JsonNode? TryGet(JsonObject args, string property)
    {
        return args.TryGetPropertyValue(property, out var node) ? node : null;
    }

    private static string? ReadString(JsonObject args, string property)
    {
        return TryGet(args, property) is JsonValue value && value.TryGetValue(out string? result) ? result : null;
    }

    private static bool? ReadBool(JsonObject args, string property)
    {
        return TryGet(args, property) is JsonValue value && value.TryGetValue(out bool result) ? result : null;
    }

    private static int? ReadInt(JsonObject args, string property)
    {
        if (TryGet(args, property) is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue(out int intValue))
        {
            return intValue;
        }

        if (value.TryGetValue(out string? stringValue) && int.TryParse(stringValue, out intValue))
        {
            return intValue;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string?> BuildQueryParameters(JsonObject args)
    {
        if (!args.TryGetPropertyValue("extras", out var extrasNode) || extrasNode is not JsonObject extras)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in extras)
        {
            if (kv.Value is JsonValue jsonValue && jsonValue.TryGetValue(out string? str))
            {
                dict[kv.Key] = str;
            }
            else if (kv.Value is not null)
            {
                dict[kv.Key] = kv.Value.ToJsonString();
            }
        }

        return dict;
    }

    private static SortSpec ParseSort(string sort)
    {
        var parts = sort.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return new SortSpec(sort, false);
        }

        var field = parts[0];
        var desc = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return new SortSpec(field, desc);
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName);
        property?.SetValue(target, value);
    }

    private static object ConvertValue(JsonNode node, Type targetType)
    {
        if (targetType == typeof(string) && node is JsonValue strValue && strValue.TryGetValue(out string? strResult))
        {
            return strResult ?? string.Empty;
        }

        if (targetType.IsEnum)
        {
            var enumText = node is JsonValue enumValue && enumValue.TryGetValue(out string? enumString)
                ? enumString
                : node.ToJsonString().Trim('"');
            return Enum.Parse(targetType, enumText, ignoreCase: true);
        }

        if (targetType == typeof(Guid))
        {
            var text = node is JsonValue guidValue && guidValue.TryGetValue(out string? guidString) ? guidString : node.ToJsonString().Trim('"');
            return Guid.Parse(text!);
        }

        return node.Deserialize(targetType, SerializerOptions)
               ?? throw new JsonException($"Unable to convert value to {targetType.Name}.");
    }

    private static object ConvertEntity(JsonNode node, Type entityType)
    {
        return node.Deserialize(entityType, SerializerOptions)
               ?? throw new JsonException($"Unable to deserialize payload as {entityType.Name}.");
    }

    private static object ConvertEntityCollection(JsonNode node, Type entityType)
    {
        var listType = typeof(List<>).MakeGenericType(entityType);
        return node.Deserialize(listType, SerializerOptions)
               ?? throw new JsonException($"Unable to deserialize collection payload as {entityType.Name} list.");
    }

    private static object ConvertKeyCollection(JsonNode node, Type keyType)
    {
        if (node is not JsonArray array)
        {
            throw new JsonException("Expected an array of identifiers.");
        }

        var listType = typeof(List<>).MakeGenericType(keyType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in array)
        {
            if (item is null)
            {
                throw new JsonException("Identifier array contains null entry.");
            }

            list.Add(ConvertValue(item, keyType));
        }

        return list;
    }

    private static object ConvertPatchDocument(JsonNode node, Type entityType)
    {
        var json = node.ToJsonString();
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        var patchType = typeof(JsonPatchDocument<>).MakeGenericType(entityType);
        var document = JsonConvert.DeserializeObject(json, patchType, settings);
        if (document is null)
        {
            throw new JsonException("Unable to deserialize JSON Patch document.");
        }

        return document;
    }
}

public sealed record RequestTranslation(string MethodName, object Request);
