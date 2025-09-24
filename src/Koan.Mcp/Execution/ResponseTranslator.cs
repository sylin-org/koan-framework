using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Koan.Web.Endpoints;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Mcp.Execution;

public sealed class ResponseTranslator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public McpToolExecutionResult Translate(McpEntityRegistration registration, McpToolDefinition tool, EntityEndpointResult result)
    {
        if (registration is null) throw new ArgumentNullException(nameof(registration));
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (result is null) throw new ArgumentNullException(nameof(result));

        var payload = SerializePayload(result);
        var shortCircuit = SerializeShortCircuit(result);
        var headers = result.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
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

            case UnauthorizedResult:
                obj["statusCode"] = 401;
                break;

            case EmptyResult:
                obj["statusCode"] = 204;
                break;

            case StatusCodeResult statusCodeResult:
                obj["statusCode"] = statusCodeResult.StatusCode;
                break;

            case RedirectResult redirectResult:
                obj["statusCode"] = redirectResult.Permanent ? 301 : 302;
                obj["location"] = redirectResult.Url ?? string.Empty;
                obj["permanent"] = redirectResult.Permanent;
                break;

            case RedirectToRouteResult routeResult:
                obj["statusCode"] = routeResult.Permanent ? 301 : 302;
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

            default:
                if (actionResult is FileResult fileResult)
                {
                    obj["statusCode"] = 200;
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
            return JsonSerializer.SerializeToNode(value, value.GetType(), SerializerOptions);
        }
        catch
        {
            return JsonValue.Create(value.ToString());
        }
    }
}
