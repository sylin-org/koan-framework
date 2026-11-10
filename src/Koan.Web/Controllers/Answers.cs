using System.Globalization;
using System.Security.Claims;
using System.Text;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Lifecycle;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Koan.Web.Controllers;

/// <summary>A route's one answer to a command's outcome (ARCH-0141, ARCH-0143, ARCH-0145). A refusal's kind picks its status once, here, and its
/// answer is a <see cref="Problem"/> carrying the reason's word, and its field where it names one; what success answers stays the action's, and
/// every page stays the application's. A <c>401</c> carries each <see cref="IChallenge"/> the application registered (ARCH-0146), as every
/// <c>401</c> an action answers does.
/// <list type="table">
/// <item><term><see cref="ReasonKind.Invalid"/></term><description>400, with the reason, under its field in <c>errors</c> where it names one.</description></item>
/// <item><term><see cref="ReasonKind.Absent"/></term><description>404, saying <see cref="Nothing"/> whatever the reason says, so nothing an application words tells a hidden resource from a missing one.</description></item>
/// <item><term><see cref="ReasonKind.Gated"/></term><description>401 to someone not signed in, otherwise 403, with the reason.</description></item>
/// <item><term><see cref="ReasonKind.Conflict"/></term><description>409, with the reason.</description></item>
/// <item><term>Failed</term><description>503 when it never ran, otherwise 500, naming no cause.</description></item>
/// </list></summary>
public static class Answers
{
    /// <summary>What every door says for anything absent.</summary>
    public const string Nothing = "Nothing answers at this address.";

    /// <summary>Answers <paramref name="done"/> for a command that committed, and the one mapping otherwise.</summary>
    public static IActionResult Answer<TResult>(this ControllerBase controller, Outcome<TResult> outcome, Func<TResult, IActionResult> done)
        => outcome is Outcome<TResult>.Done committed ? done(committed.Result) : NotDone(controller.HttpContext, outcome);

    /// <inheritdoc cref="Answer{TResult}(ControllerBase, Outcome{TResult}, Func{TResult, IActionResult})"/>
    public static async Task<IActionResult> Answer<TResult>(this ControllerBase controller, Outcome<TResult> outcome, Func<TResult, Task<IActionResult>> done)
        => outcome is Outcome<TResult>.Done committed ? await done(committed.Result) : NotDone(controller.HttpContext, outcome);

    /// <summary>
    /// The one answer to a write that committed (WEB-0088), at <paramref name="location"/>, the address of what it wrote as a path under
    /// the application's base, as <c>AddressOf</c> names a row's: the base is put before it here. It is never cached. A browser's form is
    /// sent on (<c>303</c>) to the safe path its <c>return</c> field names under the base, or else to <paramref name="location"/>, so the
    /// page it came from posts, redirects and gets; a sender that reads JSON (<see cref="WantsJson"/>) reads <paramref name="body"/>,
    /// <c>201</c> where the write <paramref name="created"/> it and <c>200</c> otherwise, with <paramref name="location"/> as its
    /// <c>Location</c>. What a header cannot carry of either path, a letter of another script among it, is sent percent-encoded.
    /// </summary>
    public static IActionResult Written(this ControllerBase controller, string location, object? body, bool created)
    {
        ArgumentNullException.ThrowIfNull(location);
        var http = controller.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        var at = Carried(location.StartsWith('/') && !location.StartsWith("//", StringComparison.Ordinal)
            ? http.Request.PathBase.ToUriComponent() + location
            : location);
        if (!WantsJson(http.Request)) return new SentOn(at);

        http.Response.Headers.Location = at;
        return new ObjectResult(body)
        {
            StatusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            ContentTypes = { KoanWebConstants.ContentTypes.ApplicationJson }
        };
    }

    /// <summary>
    /// Whether the sender reads JSON rather than pages: it sent JSON, or its <c>Accept</c> names JSON and not HTML, or it sent no form and
    /// its <c>Accept</c> names no HTML. A browser's form, and a request that asks for HTML, read pages.
    /// </summary>
    public static bool WantsJson(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HasJsonContentType()) return true;
        var (json, html) = Asks(request);
        if (json && !html) return true;
        return !html && !request.HasFormContentType;
    }

    /// <summary>Whether the sender's <c>Accept</c> names JSON and not HTML: a client of the API, every tool call among them, as against a
    /// browser, a link preview or a client that names no format.</summary>
    internal static bool NamesJson(HttpRequest request)
    {
        var (json, html) = Asks(request);
        return json && !html;
    }

    /// <summary>Whether the sender's <c>Accept</c> names JSON, and whether it names HTML.</summary>
    private static (bool Json, bool Html) Asks(HttpRequest request)
    {
        var accept = request.GetTypedHeaders().Accept;
        return (accept.Any(type => type.MediaType.Equals(KoanWebConstants.ContentTypes.ApplicationJson, StringComparison.OrdinalIgnoreCase)
                                   || type.Suffix.Equals("json", StringComparison.OrdinalIgnoreCase)),
                accept.Any(type => type.MediaType.Equals(Html, StringComparison.OrdinalIgnoreCase)));
    }

    private const string Html = "text/html";

    /// <summary>
    /// A read sent on to its own address with only the words its operation takes (WEB-0090), as <paramref name="taken"/> holds them, each
    /// value and each repeat as it was sent: <c>302</c>, never <c>301</c>, and nothing said of how long it holds, since the words an address
    /// takes may change. The address is the request's own under the base, as a header carries it; null where that path is not a path here,
    /// so a read is never sent on to another host's address.
    /// </summary>
    internal static IActionResult? Without(HttpContext http, IEnumerable<KeyValuePair<string, StringValues>> taken)
    {
        var path = http.Request.PathBase.ToUriComponent() + http.Request.Path.ToUriComponent();
        return Safe(path) ? new RedirectResult(Carried(path) + QueryString.Create(taken).ToUriComponent(), permanent: false) : null;
    }

    /// <summary>A form sent on: to the path its <c>return</c> field names under the application's base, where it is one — a single slash
    /// and a path, never another host's (<c>//</c>), a backslash, a control character or a <c>.</c> or <c>..</c> segment, which would
    /// climb out of the base — and otherwise to the address written. The form is read as the answer is written, so an action that bound
    /// none of it answers alike.</summary>
    private sealed class SentOn(string location) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var request = context.HttpContext.Request;
            var onward = location;
            if (request.HasFormContentType
                && (await request.ReadFormAsync(context.HttpContext.RequestAborted))[KoanWebConstants.Form.Return].ToString() is { Length: > 0 } path
                && Safe(path))
                onward = Carried(request.PathBase.ToUriComponent() + path);
            context.HttpContext.Response.Headers.Location = onward;
            context.HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        }
    }

    /// <summary>Whether <paramref name="path"/> is a path here: a single slash and a path, never another host's (<c>//</c>), a backslash, a
    /// control character or a <c>.</c> or <c>..</c> segment, escaped or not, which would climb out of the base.</summary>
    private static bool Safe(string path)
    {
        if (path.Length == 0 || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\') || path.Any(char.IsControl)) return false;
        var end = path.IndexOfAny(['?', '#']);
        foreach (var segment in (end < 0 ? path : path[..end]).Split('/'))
            if (segment.Replace("%2e", ".", StringComparison.OrdinalIgnoreCase) is "." or "..") return false;
        return true;
    }

    /// <summary>A path as a header carries it: each character outside printable ASCII as the percent-escapes of its UTF-8 bytes, and every
    /// escape already in it as it was.</summary>
    private static string Carried(string path)
    {
        if (path.All(c => c is > ' ' and < '\x7f')) return path;
        var carried = new StringBuilder(path.Length * 3);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in path.EnumerateRunes())
        {
            if (rune.Value is > ' ' and < 0x7f)
            {
                carried.Append((char)rune.Value);
                continue;
            }
            var length = rune.EncodeToUtf8(bytes);
            foreach (var octet in bytes[..length]) carried.Append('%').Append(octet.ToString("X2", CultureInfo.InvariantCulture));
        }
        return carried.ToString();
    }

    /// <summary>The one mapping of a reason.</summary>
    public static IActionResult Refused(this ControllerBase controller, Reason reason) => Refused(controller.HttpContext, reason);

    /// <inheritdoc cref="Refused(ControllerBase, Reason)"/>
    public static IActionResult Refused(HttpContext http, Reason reason) => Refused(http.User, reason);

    /// <summary>The one mapping of a reason, for a door that knows its reader and holds no request, as an entity write's endpoint and an
    /// entity tool do.</summary>
    public static IActionResult Refused(ClaimsPrincipal? reader, Reason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var detail = reason.Kind == ReasonKind.Absent ? Nothing : reason.Text;
        return new Problem(Status(reader, reason), detail, reason.Word,
            reason.Field is { } field ? new Dictionary<string, string[]> { [field] = [detail] } : null).ToResult();
    }

    /// <summary>A lifecycle handler's cancellation, which refuses the row as it stands: a conflict, with the handler's code as its word.</summary>
    internal static IActionResult Refused(ClaimsPrincipal? reader, EntityLifecycleCancelledException cancelled)
        => Refused(reader, string.IsNullOrWhiteSpace(cancelled.ReasonCode)
            ? Reason.Conflict(cancelled.Message)
            : Reason.Conflict(cancelled.ReasonCode, cancelled.Message));

    /// <summary>The status a reason answers with, for a page that shows it in place.</summary>
    public static int Status(HttpContext http, Reason reason) => Status(http.User, reason);

    private static int Status(ClaimsPrincipal? reader, Reason reason) => reason.Kind switch
    {
        ReasonKind.Absent => StatusCodes.Status404NotFound,
        ReasonKind.Gated => reader?.Identity?.IsAuthenticated == true ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized,
        ReasonKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>The one mapping of an outcome that is not done.</summary>
    public static IActionResult NotDone<TResult>(HttpContext http, Outcome<TResult> outcome)
    {
        switch (outcome)
        {
            case Outcome<TResult>.Refused refused:
                return Refused(http, refused.Reason);
            case Outcome<TResult>.Failed { Landed: DataCommitOutcome.NotDispatched } failed:
                Log(http, failed.Cause);
                return NeverRan(after => http.Response.Headers.RetryAfter = after);
            case Outcome<TResult>.Failed failed:
                Log(http, failed.Cause);
                return new Problem(StatusCodes.Status500InternalServerError,
                    failed.Landed == DataCommitOutcome.Unknown ? "It may or may not have been done, so look before sending it again." : "It failed, and nothing was done.").ToResult();
            default:
                throw new InvalidOperationException($"A command answered {outcome}.");
        }
    }

    /// <summary>What a write that never ran answers, a command's or an entity's (WEB-0086): 503, safe to send again, and when, through
    /// <paramref name="retryAfter"/>, which puts the <c>Retry-After</c> where the door that answers carries its headers.</summary>
    internal static IActionResult NeverRan(Action<string> retryAfter)
    {
        retryAfter("1");
        return new Problem(StatusCodes.Status503ServiceUnavailable, "It never ran, so nothing was done and it is safe to send again.").ToResult();
    }

    internal static IActionResult TooLarge()
        => new Problem(StatusCodes.Status413PayloadTooLarge, "This is more than this address takes, so nothing was done.").ToResult();

    /// <summary>What a gate's decision that is not an allow answers: a refusal with its reason as any refusal is, otherwise one of three fixed
    /// sentences.</summary>
    internal static IActionResult Denied(HttpContext http, AuthorizeDecision decision) => decision switch
    {
        AuthorizeDecision.Refuse refuse => Refused(http, refuse.Reason),
        AuthorizeDecision.Absent => new Problem(StatusCodes.Status404NotFound, Nothing).ToResult(),
        AuthorizeDecision.Challenge => new Problem(StatusCodes.Status401Unauthorized, "Sign in to do this.").ToResult(),
        _ => new Problem(StatusCodes.Status403Forbidden, "This isn't open to you.").ToResult(),
    };

    /// <summary>What a request whose binding failed answers: a body past its route's limit is too large, since ASP.NET's form provider
    /// reports one as a model error and its length says which it was; otherwise each field binding could not take.</summary>
    internal static IActionResult Unbound(HttpContext http, ModelStateDictionary state)
        => http.Request.ContentLength > http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize
            ? TooLarge()
            : new Problem(StatusCodes.Status400BadRequest, Errors: new ValidationProblemDetails(state).Errors.AsReadOnly()).ToResult();

    private static void Log(HttpContext http, Exception cause)
        => http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(Answers))
            .LogError(cause, "A command failed at {Path}", http.Request.Path);
}
