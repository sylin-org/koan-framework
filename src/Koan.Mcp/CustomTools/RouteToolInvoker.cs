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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// A tool call as a request (ARCH-0141): the arguments become a path, a query and a JSON body, and Koan.Web's pipeline answers it through the
/// application's whole pipeline as the caller of the request in hand (ARCH-0144): routing, authentication, authorization, the limiter, its own
/// middleware, the filters, the action. Nothing here decides what a route would. A value for the path or the query is the type the route
/// takes, or refused on its field; where the application reads by address, an <c>address</c> names the route's values at once (AI-0042).
/// Every answer is the edge's one mapping (<see cref="ToolAnswers"/>).
/// </summary>
internal sealed class RouteToolInvoker(KoanWebPipeline pipeline, KoanWebBaseUrl baseUrl, IHttpContextAccessor accessor, ILogger<RouteToolInvoker> logger)
{
    public async Task<McpRpcHandler.CallToolResult> Invoke(McpCustomTool tool, JObject? arguments, ClaimsPrincipal? user, CancellationToken ct)
    {
        var route = tool.Route ?? throw new ArgumentException($"{tool.Name} has no route.", nameof(tool));
        var writes = !HttpMethods.IsGet(route.HttpMethod) && !HttpMethods.IsHead(route.HttpMethod);
        var fields = (JObject?)arguments?.DeepClone() ?? new JObject();
        fields.Remove(McpDryRun.ArgumentName);
        var types = tool.InputSchema["properties"] as JObject;

        // An address names the route's values, and its query the query's; a value named beside it is its own, and must agree.
        Dictionary<string, string>? named = null;
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues>? asked = null;
        if (route.TakesAddress && fields.TryGetValue(AddressReader.Field, StringComparison.OrdinalIgnoreCase, out var given) && given.Type != JTokenType.Null)
        {
            fields.Remove(((JProperty)given.Parent!).Name);
            if (given.Type != JTokenType.String) return ToolAnswers.Invalid(tool.Name, AddressReader.Field, "An address is a string.", writes);
            if (!Addresses.TryLocal(given.Value<string>()!, accessor.HttpContext, baseUrl, out var local, out var refusal))
                return ToolAnswers.Invalid(tool.Name, AddressReader.Field, refusal, writes);
            var split = local.IndexOf('?');
            named = Match(route.Pattern, split < 0 ? local : local[..split]);
            if (named is null)
                return ToolAnswers.Invalid(tool.Name, AddressReader.Field, $"'{given.Value<string>()}' names nothing {tool.Name} acts on, which is an address such as /{route.Pattern.RawText?.TrimStart('/')}.", writes);
            if (split >= 0) asked = QueryHelpers.ParseQuery(local[split..]);
            foreach (var (name, value) in named)
                if (fields.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var also) && Text(also) is { } stated && stated != value)
                    return ToolAnswers.Invalid(tool.Name, name, $"The address names {value}, and this says {stated}.", writes);
        }

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
                    if (Wrong(fields, parameter.Name, types) is { } wrong) return ToolAnswers.Invalid(tool.Name, parameter.Name, wrong, writes);
                    var value = Take(fields, parameter.Name) ?? (named?.GetValueOrDefault(parameter.Name) is { Length: > 0 } fromAddress ? fromAddress : null) ?? parameter.Default?.ToString();
                    if (value is null)
                    {
                        if (!parameter.IsOptional) return ToolAnswers.Invalid(tool.Name, parameter.Name, "A value is required.", writes);
                        continue;
                    }

                    // HTTP resolves a dot segment before any route sees it, so no action is ever told one.
                    string[] pieces = parameter.IsCatchAll ? value.Split('/') : [value];
                    if (pieces.Any(piece => piece is "." or ".."))
                        return ToolAnswers.Refused(tool.Name, new Problem(StatusCodes.Status404NotFound, Answers.Nothing), writes);
                    text.Append(string.Join('/', pieces.Select(Uri.EscapeDataString)));
                }
            }

            if (text.Length > 0) path.Append('/').Append(text);
        }

        var query = new QueryBuilder();
        foreach (var name in route.QueryFields)
        {
            if (fields.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
            {
                if (Wrong(fields, name, types) is { } wrong) return ToolAnswers.Invalid(tool.Name, name, wrong, writes);
                foreach (var item in token is JArray many ? many : (IEnumerable<JToken>)[token]) query.Add(name, Text(item) ?? "");
                fields.Remove(((JProperty)token.Parent!).Name);
            }
            else if (asked?.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) is { Key: not null } pair)
                foreach (var item in pair.Value) query.Add(name, item ?? "");
        }

        JToken? content = route.BodyField is { } whole ? fields.GetValue(whole, StringComparison.OrdinalIgnoreCase) : fields;
        var body = content is null ? [] : Encoding.UTF8.GetBytes(content.ToString(Formatting.None));
        if (route.Action.EndpointMetadata.OfType<IRequestSizeLimitMetadata>().LastOrDefault()?.MaxRequestBodySize is { } limit && body.Length > limit)
            return ToolAnswers.Refused(tool.Name, new Problem(StatusCodes.Status413PayloadTooLarge, $"The arguments are larger than the {limit} bytes this takes."), writes);

        var request = new InProcessRequest(route.HttpMethod, (path.Length > 0 ? path.ToString() : "/") + query.ToQueryString()) { Body = writes ? body : default, Principal = user };
        request.Headers.Accept = "application/json";
        if (writes) request.Headers.ContentType = "application/json";
        request.Features.Set(route);
        foreach (var name in AddressReader.Transport) request.Withheld.Add(name);

        try
        {
            return ToolAnswers.Of(tool.Name, await pipeline.Send(request, ct).ConfigureAwait(false), writes);
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            logger.LogError(failure, "The route of tool {Tool} failed.", tool.Name);
            return ToolAnswers.Refused(tool.Name, new Problem(StatusCodes.Status500InternalServerError, "It failed."), writes);
        }
    }

    /// <summary>The route's values an address names: its segments matched against the route's from the start, a literal by itself and a
    /// value taking one segment, a catch-all the rest. What the route has past the address may be only its own words or values it can do
    /// without, so the address of a Topic names where a reply to it is sent.</summary>
    private static Dictionary<string, string>? Match(RoutePattern pattern, string path)
    {
        var given = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var at = 0;
        foreach (var segment in pattern.PathSegments)
        {
            if (segment.Parts.Count != 1) return null;
            var part = segment.Parts[0];
            if (at >= given.Length)
            {
                if (part is RoutePatternLiteralPart || part is RoutePatternParameterPart { IsOptional: true } or RoutePatternParameterPart { IsCatchAll: true }
                    || part is RoutePatternParameterPart { Default: not null }) continue;
                return null;
            }

            switch (part)
            {
                case RoutePatternLiteralPart literal when string.Equals(literal.Content, given[at], StringComparison.OrdinalIgnoreCase):
                    at++;
                    break;
                case RoutePatternParameterPart { IsCatchAll: true } rest:
                    values[rest.Name] = string.Join('/', given[at..]);
                    at = given.Length;
                    break;
                case RoutePatternParameterPart parameter:
                    values[parameter.Name] = given[at++];
                    break;
                default:
                    return null;
            }
        }

        return at == given.Length ? values : null;
    }

    /// <summary>What a value for the path or the query is refused for, where it is not the type its schema says the route takes.</summary>
    private static string? Wrong(JObject fields, string name, JObject? types)
    {
        if (!fields.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) || token.Type == JTokenType.Null) return null;
        return types?[((JProperty)token.Parent!).Name]?["type"]?.Value<string>() switch
        {
            "string" when token.Type != JTokenType.String => "A string is expected here.",
            "integer" when token.Type != JTokenType.Integer => "A whole number is expected here.",
            "number" when token.Type is not (JTokenType.Integer or JTokenType.Float) => "A number is expected here.",
            "boolean" when token.Type != JTokenType.Boolean => "True or false is expected here.",
            "array" => null,
            _ when token.Type is JTokenType.Object or JTokenType.Array => "One value is expected here.",
            _ => null,
        };
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
}
