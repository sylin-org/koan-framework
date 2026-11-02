using System.Security.Claims;
using System.Text;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Web.Authorization;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// A tool call as a request to its route (ARCH-0141): the arguments become the route values, query and JSON body, MVC runs the
/// action with its binding, filters and gate, and what it answers becomes the result. Reach it through the action and nothing
/// can differ but what the request says it is. This is the MCP surface's one mapping: a status of 400 and over is a failed call,
/// and the answer's <c>kind</c>, or else its status, is the <c>errorCode</c>.
/// </summary>
internal sealed class RouteToolInvoker(IServiceScopeFactory scopes, IActionInvokerFactory invokers, ILogger<RouteToolInvoker> logger,
    IHttpContextAccessor? accessor = null, KoanWebBaseUrl? baseUrl = null)
{
    public async Task<McpRpcHandler.CallToolResult> Invoke(McpCustomTool tool, JObject? arguments, ClaimsPrincipal? user, CancellationToken ct)
    {
        var route = tool.Route ?? throw new ArgumentException($"{tool.Name} has no route.", nameof(tool));
        var fields = (JObject?)arguments?.DeepClone() ?? new JObject();
        fields.Remove(McpDryRun.ArgumentName);

        var values = new RouteValueDictionary();
        foreach (var (key, value) in route.Action.RouteValues) values[key] = value;
        var path = new StringBuilder();
        foreach (var segment in route.Pattern.PathSegments)
        {
            var text = new StringBuilder();
            foreach (var part in segment.Parts)
            {
                if (part is RoutePatternLiteralPart literal) text.Append(literal.Content);
                else if (part is RoutePatternSeparatorPart separator) text.Append(separator.Content);
                else if (part is RoutePatternParameterPart parameter)
                {
                    if (Take(fields, parameter.Name) is { } value)
                    {
                        values[parameter.Name] = value;
                        text.Append(Uri.EscapeDataString(value));
                    }
                    else if (!parameter.IsOptional)
                        return Refused(tool, $"{parameter.Name}: a value is required.");
                }
            }

            if (text.Length > 0) path.Append('/').Append(text);
        }

        var query = new QueryBuilder();
        foreach (var name in route.QueryFields)
        {
            if (!fields.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token)) continue;
            foreach (var item in token is JArray many ? many : (IEnumerable<JToken>)[token]) query.Add(name, Text(item) ?? "");
            fields.Remove(((JProperty)token.Parent!).Name);
        }

        var body = Encoding.UTF8.GetBytes(fields.ToString(Formatting.None));
        if (route.Action.EndpointMetadata.OfType<IRequestSizeLimitMetadata>().LastOrDefault()?.MaxRequestBodySize is { } limit && body.Length > limit)
            return Refused(tool, $"The arguments are larger than the {limit} bytes this takes.");

        var outer = accessor?.HttpContext;
        var origin = outer is null ? baseUrl?.Current is { } current ? new Uri(current) : null : null;
        await using var scope = scopes.CreateAsyncScope();
        using var answer = new MemoryStream();
        var http = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = user ?? new ClaimsPrincipal(),
            RequestAborted = ct,
        };
        http.Request.Method = route.HttpMethod;
        http.Request.Scheme = outer?.Request.Scheme ?? origin?.Scheme ?? "http";
        http.Request.Host = outer?.Request.Host ?? (origin is null ? new HostString("localhost") : new HostString(origin.Authority));
        http.Request.PathBase = outer?.Request.PathBase ?? baseUrl?.PathBase ?? default;
        http.Request.Path = path.ToString();
        http.Request.QueryString = query.ToQueryString();
        http.Request.RouteValues = values;
        http.Request.Headers.Accept = "application/json";
        if (!HttpMethods.IsGet(route.HttpMethod) && !HttpMethods.IsHead(route.HttpMethod))
        {
            http.Request.ContentType = "application/json";
            http.Request.ContentLength = body.Length;
            http.Request.Body = new MemoryStream(body);
        }

        http.Response.Body = answer;
        http.Connection.RemoteIpAddress = outer?.Connection.RemoteIpAddress;
        // How the caller authenticated at the edge, so that a rejected credential is rejected here too (WEB-0083).
        if (outer?.Features.Get<IAuthenticateResultFeature>() is { } authenticated) http.Features.Set(authenticated);
        if (OriginStamp.TierOf(http.User) is { } tier) http.Features.Set(new OriginFeature(tier));
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(route.Action.EndpointMetadata), route.Action.DisplayName));

        if (accessor is not null) accessor.HttpContext = http;
        try
        {
            await invokers.CreateInvoker(new ActionContext(http, new RouteData(values), route.Action))!.InvokeAsync().ConfigureAwait(false);
            return Result(tool, http, Encoding.UTF8.GetString(answer.ToArray()));
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogError(failure, "The route of tool {Tool} failed.", tool.Name);
            return Failed(tool, StatusCodes.Status500InternalServerError, "failed", "It failed.", null);
        }
        finally
        {
            if (accessor is not null) accessor.HttpContext = outer;
        }
    }

    private static string? Take(JObject fields, string name)
    {
        if (!fields.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) || token.Type == JTokenType.Null) return null;
        fields.Remove(((JProperty)token.Parent!).Name);
        return Text(token);
    }

    private static string? Text(JToken token) => token.Type switch
    {
        JTokenType.Null => null,
        JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
        JTokenType.String or JTokenType.Integer or JTokenType.Float => token.ToString(),
        _ => token.ToString(Formatting.None),
    };

    private static McpRpcHandler.CallToolResult Result(McpCustomTool tool, HttpContext http, string text)
    {
        var status = http.Response.StatusCode;
        if (status >= StatusCodes.Status400BadRequest)
        {
            var problem = TryProblem(text);
            return Failed(tool, status, problem?["kind"]?.Value<string>() ?? KindOf(status), Say(problem) ?? (string.IsNullOrWhiteSpace(text) ? $"Refused ({status})." : text), problem);
        }

        var meta = new JObject { ["status"] = status };
        if (http.Response.Headers.Location is { Count: > 0 } location) meta["location"] = location.ToString();
        return new McpRpcHandler.CallToolResult
        {
            Content = [new McpRpcHandler.McpContent { Type = "text", Text = text }],
            Meta = meta,
        };
    }

    /// <summary>What a refusal says: its detail, each field a value could not bind to, or its title.</summary>
    private static string? Say(JObject? problem)
    {
        if (problem is null) return null;
        if (problem["errors"] is JObject errors && errors.Count > 0)
            return string.Join(' ', errors.Properties().Select(field => string.IsNullOrEmpty(field.Name)
                ? string.Join(' ', field.Value.Values<string>())
                : $"{field.Name}: {string.Join(' ', field.Value.Values<string>())}"));
        return problem["detail"]?.Value<string>() ?? problem["title"]?.Value<string>();
    }

    private static JObject? TryProblem(string text)
    {
        try { return JToken.Parse(text) as JObject; }
        catch (JsonException) { return null; }
    }

    private static string KindOf(int status) => status switch
    {
        StatusCodes.Status400BadRequest or StatusCodes.Status413PayloadTooLarge or StatusCodes.Status415UnsupportedMediaType or StatusCodes.Status422UnprocessableEntity => "invalid",
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "gated",
        StatusCodes.Status404NotFound => "absent",
        StatusCodes.Status409Conflict => "conflict",
        _ => "failed",
    };

    private static McpRpcHandler.CallToolResult Refused(McpCustomTool tool, string text)
        => Failed(tool, StatusCodes.Status400BadRequest, "invalid", text, null);

    private static McpRpcHandler.CallToolResult Failed(McpCustomTool tool, int status, string kind, string text, JObject? problem)
    {
        var meta = new JObject { ["errorCode"] = kind, ["status"] = status, ["tool"] = tool.Name };
        if (problem is not null) meta["problem"] = problem;
        return new McpRpcHandler.CallToolResult
        {
            IsError = true,
            Content = [new McpRpcHandler.McpContent { Type = "text", Text = text }],
            Meta = meta,
        };
    }
}
