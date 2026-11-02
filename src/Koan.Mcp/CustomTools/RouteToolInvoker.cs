using System.Security.Claims;
using System.Text;
using Koan.Data.Core.Commands;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Web.Authorization;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// A tool call as a request (ARCH-0141). The arguments become a path, a query and a JSON body, and the request goes through the application's whole
/// pipeline, on a flow of its own as a request from a server is: routing, authentication, authorization, the limiter, its own middleware, the filters, the
/// action. Nothing here decides what a route would. The request is the caller's, without its credential or transport headers, with the principal the MCP edge
/// established. This is the MCP surface's one mapping: a status of 400 and over is a failed call, and the answer's <c>kind</c>, or else its status, is the <c>errorCode</c>.
/// </summary>
internal sealed class RouteToolInvoker(RoutePipeline pipeline, IServiceScopeFactory scopes, ILogger<RouteToolInvoker> logger,
    IHttpContextAccessor? accessor = null, KoanWebBaseUrl? baseUrl = null)
{
    /// <summary>What only the HTTP connection means, or what the edge has already spent.</summary>
    private static readonly HashSet<string> Withheld = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Accept", "Accept-Encoding", "Content-Type", "Content-Length", "Transfer-Encoding", "Connection", "Upgrade", "Expect", "Host", "Origin",
        "Mcp-Session-Id", "MCP-Protocol-Version", "Last-Event-ID",
    };

    public async Task<McpRpcHandler.CallToolResult> Invoke(McpCustomTool tool, JObject? arguments, ClaimsPrincipal? user, CancellationToken ct)
    {
        if (pipeline.Request is not { } serve) return Failed(tool, StatusCodes.Status503ServiceUnavailable, "failed", "This process serves no requests.", null);
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
                        if (!parameter.IsOptional) return Failed(tool, StatusCodes.Status400BadRequest, "invalid", $"{parameter.Name}: a value is required.", null);
                        continue;
                    }

                    // HTTP resolves a dot segment before any route sees it, so no action is ever told one.
                    string[] pieces = parameter.IsCatchAll ? value.Split('/') : [value];
                    if (pieces.Any(piece => piece is "." or "..")) return Failed(tool, StatusCodes.Status404NotFound, "absent", Reason.Absent("").Said, null);
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
            return Failed(tool, StatusCodes.Status413PayloadTooLarge, "invalid", $"The arguments are larger than the {limit} bytes this takes.", null);

        var outer = accessor?.HttpContext;
        var origin = outer is null && baseUrl?.Current is { } current ? new Uri(current) : null;
        await using var scope = scopes.CreateAsyncScope();
        using var answer = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = user ?? new ClaimsPrincipal(), RequestAborted = ct };
        if (outer is not null)
            foreach (var (name, value) in outer.Request.Headers)
                if (!Withheld.Contains(name)) http.Request.Headers[name] = value;
        http.Request.Method = route.HttpMethod;
        // With no connection there is no scheme to go by; a request from this process is as safe as one on TLS.
        http.Request.Scheme = outer?.Request.Scheme ?? origin?.Scheme ?? Uri.UriSchemeHttps;
        http.Request.Host = outer?.Request.Host ?? (origin is null ? new HostString("localhost") : new HostString(origin.Authority));
        http.Request.Path = PathString.FromUriComponent((outer?.Request.PathBase ?? baseUrl?.PathBase ?? default).ToUriComponent() + path);
        http.Request.QueryString = query.ToQueryString();
        http.Request.Headers.Accept = "application/json";
        if (!HttpMethods.IsGet(route.HttpMethod) && !HttpMethods.IsHead(route.HttpMethod))
        {
            http.Request.ContentType = "application/json";
            http.Request.ContentLength = body.Length;
            http.Request.Body = new MemoryStream(body);
        }

        http.Response.Body = answer;
        http.Connection.RemoteIpAddress = outer?.Connection.RemoteIpAddress;
        http.Features.Set(route);
        if (OriginStamp.TierOf(http.User) is { } tier) http.Features.Set(new OriginFeature(tier));
        // What ASP.NET's policy evaluator goes by, for 403 or 401. The edge's result for a principal it accepted, which the application's default scheme might refuse. WEB-0083: a
        // credential rejected at the edge with nothing to replace it is rejected here too. The ticket names the scheme that established the caller, as a request of its own would say.
        if (http.User.Identity is { IsAuthenticated: true, AuthenticationType: var type })
            http.Features.Set<IAuthenticateResultFeature>(new Authenticated(AuthenticateResult.Success(new AuthenticationTicket(http.User,
                outer?.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Ticket?.AuthenticationScheme ?? type ?? "mcp"))));
        else if (outer?.Features.Get<IAuthenticateResultFeature>() is { AuthenticateResult.Failure: not null } rejected)
            http.Features.Set(rejected);

        try
        {
            Task served;
            using (ExecutionContext.SuppressFlow())
                served = Task.Run(async () =>
                {
                    if (accessor is not null) accessor.HttpContext = http;
                    try { await serve(http); }
                    finally { if (accessor is not null) accessor.HttpContext = null; }
                }, CancellationToken.None);
            await served.ConfigureAwait(false);
            return Result(tool, http, Encoding.UTF8.GetString(answer.ToArray()));
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            logger.LogError(failure, "The route of tool {Tool} failed.", tool.Name);
            return Failed(tool, StatusCodes.Status500InternalServerError, "failed", "It failed.", null);
        }
    }

    internal sealed class Authenticated(AuthenticateResult result) : IAuthenticateResultFeature
    {
        public AuthenticateResult? AuthenticateResult { get; set; } = result;
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

    private static McpRpcHandler.CallToolResult Result(McpCustomTool tool, HttpContext http, string text)
    {
        var status = http.Response.StatusCode;
        var json = TryJson(text);
        if (status >= StatusCodes.Status400BadRequest)
        {
            var problem = json as JObject;
            return Failed(tool, status, problem?["kind"]?.Value<string>() ?? KindOf(status), Say(status, problem) ?? (string.IsNullOrWhiteSpace(text) ? $"Refused ({status})." : text), problem);
        }

        var meta = new JObject { ["status"] = status };
        var location = http.Response.Headers.Location.ToString();
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
        if (status == StatusCodes.Status404NotFound) return Reason.Absent("").Said;
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

    private static string KindOf(int status) => status switch
    {
        StatusCodes.Status400BadRequest or StatusCodes.Status413PayloadTooLarge or StatusCodes.Status415UnsupportedMediaType or StatusCodes.Status422UnprocessableEntity => "invalid",
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "gated",
        StatusCodes.Status404NotFound => "absent",
        StatusCodes.Status409Conflict => "conflict",
        _ => "failed",
    };

    private static McpRpcHandler.CallToolResult Failed(McpCustomTool tool, int status, string kind, string text, JObject? problem)
    {
        var meta = new JObject { ["errorCode"] = kind, ["status"] = status, ["tool"] = tool.Name };
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
