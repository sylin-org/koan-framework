using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using JsonException = System.Text.Json.JsonException;

namespace Koan.Mcp.Execution;

public sealed class EndpointToolExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly McpEntityRegistry _registry;
    private readonly ILogger<EndpointToolExecutor> _logger;

    public EndpointToolExecutor(IServiceScopeFactory scopeFactory, McpEntityRegistry registry, ILogger<EndpointToolExecutor> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<McpToolExecutionResult> ExecuteAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (!_registry.TryGetTool(toolName, out var registration, out var tool))
        {
            return McpToolExecutionResult.Failure("tool_not_found", $"Tool '{toolName}' is not registered.");
        }

        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var serviceType = typeof(IEntityEndpointService<,>).MakeGenericType(registration.EntityType, registration.KeyType);
        var service = provider.GetService(serviceType);
        if (service is null)
        {
            _logger.LogError("IEntityEndpointService<{Entity},{Key}> is not registered in the current scope.", registration.EntityType.Name, registration.KeyType.Name);
            return McpToolExecutionResult.Failure("service_unavailable", $"Entity endpoint service for '{registration.DisplayName}' is not available.");
        }

        var builder = provider.GetRequiredService<EntityRequestContextBuilder>();
        var args = arguments ?? new JsonObject();
        var context = BuildContext(builder, args, cancellationToken);

        try
        {
            return await ExecuteCoreAsync(service, registration, tool, context, args, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON payload for tool {Tool}.", toolName);
            var diagnostics = new JsonObject
            {
                ["tool"] = toolName,
                ["reason"] = "invalid_payload"
            };
            return McpToolExecutionResult.Failure("invalid_payload", ex.Message, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while executing MCP tool {Tool}.", toolName);
            var diagnostics = new JsonObject
            {
                ["tool"] = toolName,
                ["reason"] = "execution_error"
            };
            return McpToolExecutionResult.Failure("execution_error", ex.Message, diagnostics);
        }
    }

    private async Task<McpToolExecutionResult> ExecuteCoreAsync(
        object service,
        McpEntityRegistration registration,
        McpToolDefinition tool,
        EntityRequestContext context,
        JsonObject args,
        CancellationToken cancellationToken)
    {
        EntityEndpointResult endpointResult;

        switch (tool.Operation)
        {
            case EntityEndpointOperationKind.Collection:
            {
                var request = BuildCollectionRequest(context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.GetCollectionAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.Query:
            {
                var request = BuildQueryRequest(context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.QueryAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.GetNew:
            {
                var request = new EntityGetNewRequest { Context = context, Accept = ReadString(args, "accept") };
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.GetNewAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.GetById:
            {
                var request = BuildGetByIdRequest(registration, context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.GetByIdAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.Upsert:
            {
                var request = BuildUpsertRequest(registration, context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.UpsertAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.UpsertMany:
            {
                var request = BuildUpsertManyRequest(registration, context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.UpsertManyAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.Delete:
            {
                var request = BuildDeleteRequest(registration, context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.DeleteAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.DeleteMany:
            {
                var request = BuildDeleteManyRequest(registration, context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.DeleteManyAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.DeleteByQuery:
            {
                var request = BuildDeleteByQueryRequest(context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.DeleteByQueryAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.DeleteAll:
            {
                var request = new EntityDeleteAllRequest { Context = context, Set = ReadString(args, "set") };
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.DeleteAllAsync), request).ConfigureAwait(false);
                break;
            }
            case EntityEndpointOperationKind.Patch:
            {
                var request = BuildPatchRequest(registration, context, args);
                endpointResult = await InvokeServiceAsync(service, nameof(IEntityEndpointService<object, object>.PatchAsync), request).ConfigureAwait(false);
                break;
            }
            default:
                return McpToolExecutionResult.Failure("unsupported_operation", $"Operation '{tool.Operation}' is not supported.");
        }

        return TranslateResult(registration, tool, endpointResult);
    }

    private static async Task<EntityEndpointResult> InvokeServiceAsync(object service, string methodName, object request)
    {
        var method = service.GetType().GetMethod(methodName);
        if (method is null)
        {
            throw new InvalidOperationException($"Service '{service.GetType().FullName}' does not implement {methodName}.");
        }

        if (method.Invoke(service, new[] { request }) is not Task task)
        {
            throw new InvalidOperationException($"Invocation of {methodName} did not return a Task instance.");
        }

        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty?.GetValue(task) is not EntityEndpointResult result)
        {
            throw new InvalidOperationException($"Invocation of {methodName} did not yield an EntityEndpointResult.");
        }

        return result;
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

    private McpToolExecutionResult TranslateResult(McpEntityRegistration registration, McpToolDefinition tool, EntityEndpointResult result)
    {
        var payload = SerializePayload(result);
        var shortCircuit = SerializeShortCircuit(result);
        var headers = result.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var warnings = result.Warnings.ToArray();

        var diagnostics = new JsonObject
        {
            ["entity"] = registration.DisplayName,
            ["operation"] = tool.Operation.ToString(),
            ["shortCircuited"] = result.IsShortCircuited
        };

        if (shortCircuit is JsonObject shortCircuitObj && shortCircuitObj.TryGetPropertyValue("statusCode", out var statusNode) && statusNode is JsonValue statusValue && statusValue.TryGetValue(out int statusCode))
        {
            diagnostics["shortCircuitStatusCode"] = statusCode;
        }

        if (shortCircuit is JsonObject shortCircuitObj2 && shortCircuitObj2.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue typeValue && typeValue.TryGetValue(out string? actionType) && !string.IsNullOrWhiteSpace(actionType))
        {
            diagnostics["shortCircuitType"] = actionType;
        }

        if (result.GetType().IsGenericType && result.GetType().GetGenericTypeDefinition() == typeof(EntityCollectionResult<>))
        {
            if (result.GetType().GetProperty(nameof(EntityCollectionResult<object>.TotalCount))?.GetValue(result) is int totalCount)
            {
                diagnostics["totalCount"] = totalCount;
            }
        }

        return McpToolExecutionResult.SuccessResult(payload, shortCircuit, headers, warnings, diagnostics);
    }

    private static JsonNode? SerializeShortCircuit(EntityEndpointResult result)
    {
        if (!result.IsShortCircuited)
        {
            return null;
        }

        if (result.ShortCircuitResult is IActionResult actionResult)
        {
            return SerializeActionResult(actionResult);
        }

        return SerializeObject(result.ShortCircuitPayload);
    }

    private static JsonObject SerializeActionResult(IActionResult actionResult)
    {
        var obj = new JsonObject
        {
            ["type"] = actionResult.GetType().Name
        };

        switch (actionResult)
        {
            case ObjectResult objectResult:
                if (objectResult.StatusCode.HasValue)
                {
                    obj["statusCode"] = objectResult.StatusCode.Value;
                }

                if (objectResult.Value is not null)
                {
                    obj["payload"] = SerializeObject(objectResult.Value);
                }

                if (objectResult.DeclaredType is not null)
                {
                    obj["declaredType"] = objectResult.DeclaredType.Name;
                }

                break;

            case JsonResult jsonResult:
                if (jsonResult.StatusCode.HasValue)
                {
                    obj["statusCode"] = jsonResult.StatusCode.Value;
                }

                if (jsonResult.Value is not null)
                {
                    obj["payload"] = SerializeObject(jsonResult.Value);
                }

                if (!string.IsNullOrWhiteSpace(jsonResult.ContentType))
                {
                    obj["contentType"] = jsonResult.ContentType;
                }

                break;

            case ContentResult contentResult:
                obj["statusCode"] = contentResult.StatusCode ?? 200;
                obj["contentType"] = contentResult.ContentType ?? "text/plain";
                obj["content"] = contentResult.Content ?? string.Empty;
                break;

            case StatusCodeResult statusCodeResult:
                obj["statusCode"] = statusCodeResult.StatusCode;
                break;

            case RedirectResult redirectResult:
                obj["statusCode"] = redirectResult.StatusCode ?? 302;
                obj["location"] = redirectResult.Url ?? string.Empty;
                obj["permanent"] = redirectResult.Permanent;
                break;

            case RedirectToRouteResult routeResult:
                obj["statusCode"] = routeResult.StatusCode ?? 302;
                obj["routeName"] = routeResult.RouteName ?? string.Empty;
                if (routeResult.RouteValues is not null)
                {
                    obj["routeValues"] = SerializeObject(routeResult.RouteValues);
                }
                break;

            case ChallengeResult challengeResult:
                obj["statusCode"] = 401;
                var challengeSchemes = new JsonArray();
                if (challengeResult.AuthenticationSchemes is not null)
                {
                    foreach (var scheme in challengeResult.AuthenticationSchemes)
                    {
                        challengeSchemes.Add(scheme);
                    }
                }

                obj["schemes"] = challengeSchemes;
                if (challengeResult.Properties is not null)
                {
                    obj["properties"] = SerializeObject(challengeResult.Properties);
                }
                break;

            case ForbidResult forbidResult:
                obj["statusCode"] = 403;
                var forbidSchemes = new JsonArray();
                if (forbidResult.AuthenticationSchemes is not null)
                {
                    foreach (var scheme in forbidResult.AuthenticationSchemes)
                    {
                        forbidSchemes.Add(scheme);
                    }
                }

                obj["schemes"] = forbidSchemes;
                if (forbidResult.Properties is not null)
                {
                    obj["properties"] = SerializeObject(forbidResult.Properties);
                }
                break;

            case UnauthorizedResult:
                obj["statusCode"] = 401;
                break;

            case EmptyResult:
                obj["statusCode"] = 204;
                break;

            default:
                if (actionResult is FileResult fileResult)
                {
                    obj["statusCode"] = fileResult.StatusCode ?? 200;
                    obj["contentType"] = fileResult.ContentType ?? string.Empty;
                    if (!string.IsNullOrEmpty(fileResult.FileDownloadName))
                    {
                        obj["fileName"] = fileResult.FileDownloadName;
                    }
                }

                break;
        }

        return obj;
    }

    private static JsonNode? SerializePayload(EntityEndpointResult result)
    {
        if (result.Payload is not null)
        {
            return SerializeObject(result.Payload);
        }

        var resultType = result.GetType();
        if (resultType.IsGenericType)
        {
            var definition = resultType.GetGenericTypeDefinition();
            if (definition == typeof(EntityCollectionResult<>))
            {
                var items = resultType.GetProperty(nameof(EntityCollectionResult<object>.Items))?.GetValue(result);
                return SerializeObject(items);
            }

            if (definition == typeof(EntityModelResult<>))
            {
                var model = resultType.GetProperty(nameof(EntityModelResult<object>.Model))?.GetValue(result);
                return SerializeObject(model);
            }
        }

        return null;
    }

    private static JsonNode? SerializeObject(object? value)
    {
        if (value is null) return null;
        if (value is JsonNode node) return node;

        try
        {
            return System.Text.Json.JsonSerializer.SerializeToNode(value, value.GetType(), SerializerOptions);
        }
        catch
        {
            return JsonValue.Create(value.ToString());
        }
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



