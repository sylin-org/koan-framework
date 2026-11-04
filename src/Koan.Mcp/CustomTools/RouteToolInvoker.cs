using System.Security.Claims;
using System.Text;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// A tool call as a request (ARCH-0141): the arguments become a path, a query and a JSON body, and Koan.Web's pipeline answers it through the
/// application's whole pipeline as the caller of the request in hand (ARCH-0144): routing, authentication, authorization, the limiter, its own
/// middleware, the filters, the action. Nothing here decides what a route would. This is the MCP surface's one mapping: a status of 400 and
/// over is a failed call, and the answer's <c>kind</c>, or else its status, is the <c>errorCode</c>.
/// </summary>
internal sealed class RouteToolInvoker(KoanWebPipeline pipeline, ILogger<RouteToolInvoker> logger)
{
    /// <summary>The headers of MCP's own transport, which mean nothing to a route.</summary>
    private static readonly string[] Transport = ["Mcp-Session-Id", "MCP-Protocol-Version", "Last-Event-ID"];

    public async Task<McpRpcHandler.CallToolResult> Invoke(McpCustomTool tool, JObject? arguments, ClaimsPrincipal? user, CancellationToken ct)
    {
        var route = tool.Route ?? throw new ArgumentException($"{tool.Name} has no route.", nameof(tool));
        var fields = (JObject?)arguments?.DeepClone() ?? new JObject();
        fields.Remove(McpDryRun.ArgumentName);

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
                    var value = Take(fields, parameter.Name) ?? parameter.Default?.ToString();
                    if (value is null)
                    {
                        if (!parameter.IsOptional) return Failed(tool, StatusCodes.Status400BadRequest, $"{parameter.Name}: a value is required.");
                        continue;
                    }

                    // HTTP resolves a dot segment before any route sees it, so no action is ever told one.
                    string[] pieces = parameter.IsCatchAll ? value.Split('/') : [value];
                    if (pieces.Any(piece => piece is "." or "..")) return Failed(tool, StatusCodes.Status404NotFound, Answers.Nothing);
                    text.Append(string.Join('/', pieces.Select(Uri.EscapeDataString)));
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

        JToken? content = route.BodyField is { } whole ? fields.GetValue(whole, StringComparison.OrdinalIgnoreCase) : fields;
        var body = content is null ? [] : Encoding.UTF8.GetBytes(content.ToString(Formatting.None));
        if (route.Action.EndpointMetadata.OfType<IRequestSizeLimitMetadata>().LastOrDefault()?.MaxRequestBodySize is { } limit && body.Length > limit)
            return Failed(tool, StatusCodes.Status413PayloadTooLarge, $"The arguments are larger than the {limit} bytes this takes.");

        var sends = !HttpMethods.IsGet(route.HttpMethod) && !HttpMethods.IsHead(route.HttpMethod);
        var request = new InProcessRequest(route.HttpMethod, (path.Length > 0 ? path.ToString() : "/") + query.ToQueryString()) { Body = sends ? body : default, Principal = user };
        request.Headers.Accept = "application/json";
        if (sends) request.Headers.ContentType = "application/json";
        request.Features.Set(route);
        foreach (var name in Transport) request.Withheld.Add(name);

        try
        {
            return Result(tool, await pipeline.Send(request, ct).ConfigureAwait(false));
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            logger.LogError(failure, "The route of tool {Tool} failed.", tool.Name);
            return Failed(tool, StatusCodes.Status500InternalServerError, "It failed.");
        }
    }

    private static string? Take(JObject fields, string name)
    {
        if (!fields.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token)) return null;
        fields.Remove(((JProperty)token.Parent!).Name);
        return Text(token) is { Length: > 0 } text ? text : null;
    }

    private static string? Text(JToken token) => token.Type switch
    {
        JTokenType.Null => null,
        JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
        JTokenType.String or JTokenType.Integer or JTokenType.Float => token.ToString(),
        _ => token.ToString(Formatting.None),
    };

    private static McpRpcHandler.CallToolResult Result(McpCustomTool tool, InProcessAnswer answer)
    {
        var status = answer.Status;
        var text = Encoding.UTF8.GetString(answer.Body.Span);
        var json = TryJson(text);
        if (status >= StatusCodes.Status400BadRequest)
        {
            var problem = json as JObject;
            return Failed(tool, status, Say(status, problem) ?? (string.IsNullOrWhiteSpace(text) ? $"Refused ({status})." : text), problem);
        }

        var meta = new JObject { ["status"] = status };
        var location = answer.Headers.Location.ToString();
        if (location.Length > 0) meta["location"] = location;
        // An answer with nothing to read says where it went. Only an object is structured: clients' schemas refuse anything else, and the text carries the rest.
        var empty = string.IsNullOrWhiteSpace(text);
        return new McpRpcHandler.CallToolResult
        {
            Content = [new McpRpcHandler.McpContent { Type = "text", Text = !empty ? text : location.Length > 0 ? $"Answered {status}: go on to {location}." : $"Answered {status}." }],
            StructuredContent = empty && location.Length > 0 ? new JObject { ["status"] = status, ["location"] = location } : json as JObject,
            Meta = meta,
        };
    }

    /// <summary>What a refusal says: one sentence for anything absent, each field a value could not bind to, or its detail or title.</summary>
    private static string? Say(int status, JObject? problem)
    {
        if (status == StatusCodes.Status404NotFound) return Answers.Nothing;
        if (problem is null) return null;
        if (problem["errors"] is JObject errors && errors.Count > 0)
            return string.Join(' ', errors.Properties().Select(field => string.IsNullOrEmpty(field.Name)
                ? string.Join(' ', field.Value.Values<string>())
                : $"{field.Name}: {string.Join(' ', field.Value.Values<string>())}"));
        return problem["detail"]?.Value<string>() ?? problem["title"]?.Value<string>();
    }

    private static JToken? TryJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return McpJson.Parse(text); }
        catch (JsonException) { return null; }
    }

    /// <summary>A refusal of the tool call, its kind the route's own or else its status's, as a <see cref="Problem"/> has it.</summary>
    private static McpRpcHandler.CallToolResult Failed(McpCustomTool tool, int status, string text, JObject? problem = null)
    {
        var meta = new JObject { ["errorCode"] = problem?["kind"]?.Value<string>() ?? new Problem(status).Kind, ["status"] = status, ["tool"] = tool.Name };
        if (problem is not null) meta["problem"] = problem;
        return new McpRpcHandler.CallToolResult
        {
            IsError = true,
            Content = [new McpRpcHandler.McpContent { Type = "text", Text = text }],
            StructuredContent = problem,
            Meta = meta,
        };
    }
}
