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
/// <item><term><see cref="ReasonKind.Gated"/></term><description>401 to someone not signed in, otherwise 403, with the reason.</description></item>
/// <item><term><see cref="ReasonKind.Conflict"/></term><description>409, with the reason.</description></item>
/// <item><term>Failed</term><description>503 when it never ran, otherwise 500, naming no cause.</description></item>
/// </list></summary>
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
        switch (outcome)
        {
            case Outcome<TResult>.Refused { Reason: { Kind: ReasonKind.Absent } }:
                return Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.", "absent");
            case Outcome<TResult>.Refused { Reason: { Kind: ReasonKind.Gated } reason }:
                return controller.User.Identity?.IsAuthenticated == true
                    ? Problem(StatusCodes.Status403Forbidden, "Not open to you", reason.Text, "gated")
                    : Problem(StatusCodes.Status401Unauthorized, "Sign in first", reason.Text, "gated");
            case Outcome<TResult>.Refused { Reason: { Kind: ReasonKind.Conflict } reason }:
                return Problem(StatusCodes.Status409Conflict, "Conflict", reason.Text, "conflict");
            case Outcome<TResult>.Refused { Reason: var reason }:
                return Problem(StatusCodes.Status400BadRequest, "Not accepted", reason.Text, "invalid");
            case Outcome<TResult>.Failed { Landed: DataCommitOutcome.NotDispatched } failed:
                Log(controller, failed.Cause);
                controller.Response.Headers.RetryAfter = "1";
                return Problem(StatusCodes.Status503ServiceUnavailable, "Try again", "It never ran, because the store is busy or cannot take it.", "failed");
            case Outcome<TResult>.Failed failed:
                Log(controller, failed.Cause);
                return Problem(StatusCodes.Status500InternalServerError, "Failed",
                    failed.Landed == DataCommitOutcome.Unknown ? "It may or may not have landed." : "It failed, and nothing landed.", "failed");
            default:
                throw new InvalidOperationException($"A command answered {outcome}.");
        }
    }

    internal static IActionResult Denied(AuthorizeDecision decision) => decision is AuthorizeDecision.Challenge
        ? Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Sign in to do this.", "gated")
        : Problem(StatusCodes.Status403Forbidden, "Not open to you", "This isn't open to you.", "gated");

    internal static IActionResult Unbound(ModelStateDictionary state)
    {
        var problem = new ValidationProblemDetails(state) { Status = StatusCodes.Status400BadRequest, Title = "Not accepted" };
        problem.Extensions[Kind] = "invalid";
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { ProblemJson } };
    }

    private static ObjectResult Problem(int status, string title, string detail, string kind)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions[Kind] = kind;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { ProblemJson } };
    }

    private static void Log(ControllerBase controller, Exception cause)
        => controller.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(Answers))
            .LogError(cause, "A command failed at {Path}", controller.Request.Path);
}
