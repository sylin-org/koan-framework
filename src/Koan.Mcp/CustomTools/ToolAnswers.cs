using System.Text;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// The edge's one answer to what a request made in process answered, and its one refusal (ARCH-0141 rule 10, AI-0042). A status of
/// 400 and over is a failed call carrying a <see cref="Problem"/>, the route's own or, where the route answered none or the edge
/// refused before any request, one the edge words, in <c>structuredContent</c> and <c>meta.problem</c>. Beside it <c>meta</c> says the
/// status, the problem's kind as <c>errorCode</c>, the route's <c>Retry-After</c> as <c>retryAfter</c> and its <c>WWW-Authenticate</c> as
/// <c>challenge</c>, and for a tool that writes whether anything <c>landed</c>, where the edge can know it.
/// </summary>
internal static class ToolAnswers
{
    /// <summary>A problem as the web's JSON writes one: camel-cased, with nothing it leaves out.</summary>
    private static readonly JsonSerializer Wire = JsonSerializer.Create(new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        ContractResolver = new CamelCasePropertyNamesContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false } },
    });

    /// <summary>A problem as the wire carries it.</summary>
    public static JObject Json(Problem problem) => JObject.FromObject(problem, Wire);

    /// <summary>What <paramref name="tool"/> answers for <paramref name="answer"/>. <paramref name="address"/> is where the answer came
    /// from, which <c>read</c> says.</summary>
    public static McpRpcHandler.CallToolResult Of(string tool, InProcessAnswer answer, bool writes, string? address = null)
    {
        var status = answer.Status;
        var text = Text(answer);
        var json = Textual(answer) ? TryJson(text) : null;
        if (status >= StatusCodes.Status400BadRequest)
        {
            // The route's problem where it answered one, ASP.NET's own given the kind its status means; otherwise one for its status,
            // and its body stays the text.
            var problem = json is JObject said && (said.ContainsKey("kind") || said.ContainsKey("status"))
                ? said
                : Json(new Problem(status, status == StatusCodes.Status404NotFound ? Answers.Nothing : null));
            problem["kind"] ??= new Problem(status).Kind;
            var refused = Refused(tool, problem, Say(status, problem) ?? (string.IsNullOrWhiteSpace(text) ? $"Refused ({status})." : text), writes);
            if (answer.Headers.RetryAfter.ToString() is { Length: > 0 } retry) refused.Meta!["retryAfter"] = retry;
            if (answer.Headers.WWWAuthenticate.ToString() is { Length: > 0 } challenge) refused.Meta!["challenge"] = challenge;
            if (address is not null) refused.Meta!["address"] = address;
            return refused;
        }

        var meta = new JObject { ["status"] = status };
        var location = answer.Headers.Location.ToString();
        if (location.Length > 0) meta["location"] = location;
        if (address is not null) meta["address"] = address;
        if (writes) meta["landed"] = true;
        // An answer with nothing to read says where it went. Only an object is structured: clients' schemas refuse anything else, and the text carries the rest.
        var empty = string.IsNullOrWhiteSpace(text);
        return new McpRpcHandler.CallToolResult
        {
            Content = [new McpRpcHandler.McpContent { Type = "text", Text = !empty ? text : location.Length > 0 ? $"Answered {status}: go on to {location}." : $"Answered {status}." }],
            StructuredContent = empty && location.Length > 0 ? new JObject { ["status"] = status, ["location"] = location } : json as JObject,
            Meta = meta,
        };
    }

    /// <summary>A refusal the edge makes itself.</summary>
    public static McpRpcHandler.CallToolResult Refused(string tool, Problem problem, bool writes)
        => Refused(tool, Json(problem), Say(problem.Status, Json(problem)) ?? problem.Title, writes);

    /// <summary>A refusal naming the argument it refuses, as a route names the field a value could not bind to.</summary>
    public static McpRpcHandler.CallToolResult Invalid(string tool, string field, string sentence, bool writes)
        => Refused(tool, new Problem(StatusCodes.Status400BadRequest, Errors: new Dictionary<string, string[]> { [field] = [sentence] }), writes);

    /// <summary>A refused call of <paramref name="tool"/> carrying <paramref name="problem"/>, with <paramref name="text"/> to read.</summary>
    public static McpRpcHandler.CallToolResult Refused(string tool, JObject problem, string text, bool writes)
    {
        var status = problem["status"]?.Value<int>() ?? StatusCodes.Status500InternalServerError;
        var meta = new JObject { ["errorCode"] = problem["kind"]?.Value<string>() ?? new Problem(status).Kind, ["status"] = status, ["tool"] = tool, ["problem"] = problem };
        // A refusal writes nothing, and what never ran did nothing; any other failure may have done it, so it says nothing.
        if (writes && (status < StatusCodes.Status500InternalServerError || status == StatusCodes.Status503ServiceUnavailable)) meta["landed"] = false;
        return new McpRpcHandler.CallToolResult
        {
            IsError = true,
            Content = [new McpRpcHandler.McpContent { Type = "text", Text = text }],
            StructuredContent = problem,
            Meta = meta,
        };
    }

    /// <summary>What a refusal says: one sentence for anything absent, each field a value could not bind to, or its detail or title.</summary>
    internal static string? Say(int status, JObject problem)
    {
        if (status == StatusCodes.Status404NotFound) return Answers.Nothing;
        if (problem["errors"] is JObject errors && errors.Count > 0)
            return string.Join(' ', errors.Properties().Select(field => string.IsNullOrEmpty(field.Name)
                ? string.Join(' ', field.Value.Values<string>())
                : $"{field.Name}: {string.Join(' ', field.Value.Values<string>())}"));
        return problem["detail"]?.Value<string>() ?? problem["title"]?.Value<string>();
    }

    /// <summary>The body as text, or what it is where it is not text: a picture's bytes read as words would be noise.</summary>
    private static string Text(InProcessAnswer answer)
        => Textual(answer)
            ? Encoding.UTF8.GetString(answer.Body.Span)
            : $"Answered {answer.Status} with {answer.Headers.ContentType}, {answer.Body.Length} bytes, which is not text.";

    private static bool Textual(InProcessAnswer answer)
    {
        var type = answer.Headers.ContentType.ToString();
        return answer.Body.IsEmpty || type.Length == 0 || type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || type.Contains("json", StringComparison.OrdinalIgnoreCase) || type.Contains("xml", StringComparison.OrdinalIgnoreCase)
               || type.Contains("javascript", StringComparison.OrdinalIgnoreCase) || type.Contains("form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private static JToken? TryJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return McpJson.Parse(text); }
        catch (JsonException) { return null; }
    }
}
