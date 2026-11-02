using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Commands;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Controllers;

/// <summary>A route's one answer to a command's outcome (ARCH-0141). A refusal's kind picks its status once, here, and its answer is a
/// problem carrying that <c>kind</c>; what success answers stays the action's.
/// <list type="table">
/// <item><term><see cref="ReasonKind.Invalid"/></term><description>400, with the reason.</description></item>
/// <item><term><see cref="ReasonKind.Absent"/></term><description>404, in one sentence whatever the reason says, so nothing an application words tells a hidden resource from a missing one.</description></item>
/// <item><term><see cref="ReasonKind.Gated"/></term><description>401 to someone not signed in, otherwise 403, with the reason, its word as <c>gate</c> and its next step as <c>next</c>.</description></item>
/// <item><term><see cref="ReasonKind.Conflict"/></term><description>409, with the reason.</description></item>
/// <item><term>Failed</term><description>503 when it never ran, otherwise 500, naming no cause.</description></item>
/// </list>
/// A request whose <c>Accept</c> prefers HTML, as a browser's form post does, reads the problem as a page: <see cref="IProblemView"/> where the
/// application supplies one, otherwise a plain one. Everything else reads <c>application/problem+json</c>.</summary>
public static class Answers
{
    private const string Kind = "kind";
    private const string ProblemJson = "application/problem+json";

    /// <summary>Answers <paramref name="done"/> for a command that committed, and the one mapping otherwise.</summary>
    public static IActionResult Answer<TResult>(this ControllerBase controller, Outcome<TResult> outcome, Func<TResult, IActionResult> done)
        => outcome is Outcome<TResult>.Done committed ? done(committed.Result) : NotDone(controller, outcome);

    /// <inheritdoc cref="Answer{TResult}(ControllerBase, Outcome{TResult}, Func{TResult, IActionResult})"/>
    public static async Task<IActionResult> Answer<TResult>(this ControllerBase controller, Outcome<TResult> outcome, Func<TResult, Task<IActionResult>> done)
        => outcome is Outcome<TResult>.Done committed ? await done(committed.Result) : NotDone(controller, outcome);

    private static IActionResult NotDone<TResult>(ControllerBase controller, Outcome<TResult> outcome)
    {
        var http = controller.HttpContext;
        switch (outcome)
        {
            case Outcome<TResult>.Refused { Reason: { Kind: ReasonKind.Absent } reason }:
                return AbsentAnswer(http, reason);
            case Outcome<TResult>.Refused { Reason: { Kind: ReasonKind.Gated } reason }:
                return controller.User.Identity?.IsAuthenticated == true
                    ? Problem(http, StatusCodes.Status403Forbidden, reason.Heading ?? "Not open to you", reason.Text, "gated", reason)
                    : Problem(http, StatusCodes.Status401Unauthorized, reason.Heading ?? "Sign in first", reason.Text, "gated", reason);
            case Outcome<TResult>.Refused { Reason: { Kind: ReasonKind.Conflict } reason }:
                return Problem(http, StatusCodes.Status409Conflict, "Conflict", reason.Text, "conflict");
            case Outcome<TResult>.Refused { Reason: var reason }:
                return Problem(http, StatusCodes.Status400BadRequest, "Not accepted", reason.Text, "invalid");
            case Outcome<TResult>.Failed { Landed: DataCommitOutcome.NotDispatched } failed:
                Log(controller, failed.Cause);
                controller.Response.Headers.RetryAfter = "1";
                return Problem(http, StatusCodes.Status503ServiceUnavailable, "Try again", "It never ran, because the store is busy or cannot take it.", "failed");
            case Outcome<TResult>.Failed failed:
                Log(controller, failed.Cause);
                return Problem(http, StatusCodes.Status500InternalServerError, "Failed",
                    failed.Landed == DataCommitOutcome.Unknown ? "It may or may not have landed." : "It failed, and nothing landed.", "failed");
            default:
                throw new InvalidOperationException($"A command answered {outcome}.");
        }
    }

    internal static IActionResult Denied(HttpContext http, AuthorizeDecision decision) => decision switch
    {
        AuthorizeDecision.Absent => AbsentAnswer(http, Reason.Absent("")),
        AuthorizeDecision.Challenge => Problem(http, StatusCodes.Status401Unauthorized, "Sign in first", "Sign in to do this.", "gated"),
        _ => Problem(http, StatusCodes.Status403Forbidden, "Not open to you", "This isn't open to you.", "gated"),
    };

    private static IActionResult AbsentAnswer(HttpContext http, Reason reason) => Problem(http, StatusCodes.Status404NotFound, "Not found", reason.Said, "absent");

    internal static IActionResult Unbound(HttpContext http, ModelStateDictionary state)
    {
        var problem = new ValidationProblemDetails(state) { Status = StatusCodes.Status400BadRequest, Title = "Not accepted" };
        problem.Extensions[Kind] = "invalid";
        return Negotiated(http, problem);
    }

    private static IActionResult Problem(HttpContext http, int status, string title, string detail, string kind, Reason? reason = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions[Kind] = kind;
        if (reason?.Word is { } word) problem.Extensions["gate"] = word;
        if (reason?.Next is { } next) problem.Extensions["next"] = next;
        return Negotiated(http, problem);
    }

    private static IActionResult Negotiated(HttpContext http, ProblemDetails problem) => PrefersHtml(http.Request)
        ? (http.RequestServices.GetService<IProblemView>() ?? ProblemPage.Instance).Render(http, problem)
        : new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { ProblemJson } };

    /// <summary>Whether <c>Accept</c> names HTML at least as highly as it names JSON. <c>*/*</c> names neither, so a client that asks for anything reads JSON.</summary>
    private static bool PrefersHtml(HttpRequest request)
    {
        double html = 0, json = 0;
        foreach (var type in request.GetTypedHeaders().Accept)
        {
            var quality = type.Quality ?? 1;
            if (type.SubType == "html" || type.SubType == "xhtml+xml") html = Math.Max(html, quality);
            else if (type.SubType == "json" || type.Suffix == "json") json = Math.Max(json, quality);
        }

        return html > 0 && html >= json;
    }

    private static void Log(ControllerBase controller, Exception cause)
        => controller.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(Answers))
            .LogError(cause, "A command failed at {Path}", controller.Request.Path);
}
