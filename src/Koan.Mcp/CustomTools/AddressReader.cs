using System.Security.Claims;
using Koan.Mcp.Hosting;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// The edge's <c>read</c> (AI-0042), offered where <c>Koan:Mcp:ReadByAddress</c> is on: what a GET of an address of this application
/// answers, sent through its whole pipeline in process as the caller of the request in hand (ARCH-0144), as JSON where the route answers
/// in kind. With no address it reads <c>/</c>. A redirect to an address of this application is followed once; an address of another host is
/// refused, naming the field. It opens no door a route had not opened: each address answers as it answers that caller over HTTP.
/// </summary>
internal sealed class AddressReader(KoanWebPipeline pipeline, KoanWebBaseUrl baseUrl, IHttpContextAccessor accessor, ILogger<AddressReader> logger)
{
    /// <summary>The tool's name.</summary>
    public const string Name = "read";

    /// <summary>Its one argument.</summary>
    public const string Field = "address";

    /// <summary>What an address is, as every tool that takes one says it.</summary>
    public const string AddressHolds = "An address of this place: an absolute URL a reading returned, or its path, such as /t/general.";

    /// <summary>The tool as the registry lists it.</summary>
    public static McpCustomTool Tool { get; } = McpCustomToolRegistry.Assemble(Name,
        "Read what an address of this place answers, as JSON, as you: a page's own data, with the forms you may send from it. With no address, it reads the front page. A redirect within this place is followed once.",
        new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { [Field] = new JObject { ["type"] = "string", ["description"] = AddressHolds } },
        },
        typeof(AddressReader).GetMethod(nameof(Read))!, isMutation: false, gateAction: EntityAuthorizeActions.Read,
        readOnly: true, destructive: false, idempotent: true, openWorld: false, reads: true);

    /// <summary>The headers of MCP's own transport, which mean nothing to a route.</summary>
    internal static readonly string[] Transport = ["Mcp-Session-Id", "MCP-Protocol-Version", "Last-Event-ID"];

    public async Task<McpRpcHandler.CallToolResult> Read(JObject? arguments, ClaimsPrincipal? user, CancellationToken ct)
    {
        string named = "/";
        if (arguments is not null && arguments.TryGetValue(Field, StringComparison.OrdinalIgnoreCase, out var given) && given.Type != JTokenType.Null)
        {
            if (given.Type != JTokenType.String) return ToolAnswers.Invalid(Name, Field, "An address is a string.", writes: false);
            if (given.Value<string>() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text)) named = text;
        }

        var inHand = accessor.HttpContext;
        if (!Addresses.TryLocal(named, inHand, baseUrl, out var local, out var refusal))
            return ToolAnswers.Invalid(Name, Field, refusal, writes: false);

        try
        {
            var answer = await Get(local, user, ct).ConfigureAwait(false);
            // A redirect within this place is a step of the same read; one elsewhere, or a second, is answered as it is.
            if (answer.Status is StatusCodes.Status301MovedPermanently or StatusCodes.Status302Found or StatusCodes.Status303SeeOther
                    or StatusCodes.Status307TemporaryRedirect or StatusCodes.Status308PermanentRedirect
                && answer.Headers.Location.ToString() is { Length: > 0 } location
                && Addresses.TryLocal(Followed(Addresses.Absolute(local, inHand, baseUrl), location), inHand, baseUrl, out var next, out _))
            {
                local = next;
                answer = await Get(local, user, ct).ConfigureAwait(false);
            }

            return ToolAnswers.Of(Name, answer, writes: false, address: Addresses.Absolute(local, inHand, baseUrl));
        }
        catch (ArgumentException)
        {
            // An address the pipeline will not take, such as one naming an escaped dot segment, names nothing a route is told.
            return ToolAnswers.Refused(Name, new Problem(StatusCodes.Status404NotFound, Answers.Nothing), writes: false);
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            logger.LogError(failure, "Reading {Address} failed.", local);
            return ToolAnswers.Refused(Name, new Problem(StatusCodes.Status500InternalServerError, "It failed."), writes: false);
        }
    }

    private Task<InProcessAnswer> Get(string local, ClaimsPrincipal? user, CancellationToken ct)
    {
        var request = new InProcessRequest(HttpMethods.Get, local) { Principal = user };
        request.Headers.Accept = "application/json";
        foreach (var name in Transport) request.Withheld.Add(name);
        return pipeline.Send(request, ct);
    }

    /// <summary>A redirect's target, resolved against the address that answered it as HTTP resolves one.</summary>
    private static string Followed(string from, string location)
        => Uri.TryCreate(from, UriKind.Absolute, out var origin) && origin.Scheme.StartsWith(Uri.UriSchemeHttp, StringComparison.Ordinal)
            ? new Uri(origin, location).ToString()
            : Uri.TryCreate(location, UriKind.Absolute, out var elsewhere) && elsewhere.Scheme.StartsWith(Uri.UriSchemeHttp, StringComparison.Ordinal)
                ? location
                : new Uri(new Uri("http://local" + from), location).PathAndQuery;
}
