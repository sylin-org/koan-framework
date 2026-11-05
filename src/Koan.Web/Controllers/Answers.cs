using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Commands;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Controllers;

/// <summary>A route's one answer to a command's outcome (ARCH-0141, ARCH-0143, ARCH-0145). A refusal's kind picks its status once, here, and its
/// answer is a <see cref="Problem"/> carrying the reason's word, and its field where it names one; what success answers stays the action's, and
/// every page stays the application's. A <c>401</c> carries each <see cref="IChallenge"/> the application registered (ARCH-0146).
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

    /// <summary>The one mapping of a reason.</summary>
    public static IActionResult Refused(this ControllerBase controller, Reason reason) => Refused(controller.HttpContext, reason);

    /// <inheritdoc cref="Refused(ControllerBase, Reason)"/>
    public static IActionResult Refused(HttpContext http, Reason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var detail = reason.Kind == ReasonKind.Absent ? Nothing : reason.Text;
        return Answer(http, new Problem(Status(http, reason), detail, reason.Word,
            reason.Field is { } field ? new Dictionary<string, string[]> { [field] = [detail] } : null));
    }

    /// <summary>The status a reason answers with, for a page that shows it in place.</summary>
    public static int Status(HttpContext http, Reason reason) => reason.Kind switch
    {
        ReasonKind.Absent => StatusCodes.Status404NotFound,
        ReasonKind.Gated => http.User.Identity?.IsAuthenticated == true ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized,
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
                http.Response.Headers.RetryAfter = "1";
                return new Problem(StatusCodes.Status503ServiceUnavailable, "It never ran, so nothing was done and it is safe to send again.").ToResult();
            case Outcome<TResult>.Failed failed:
                Log(http, failed.Cause);
                return new Problem(StatusCodes.Status500InternalServerError,
                    failed.Landed == DataCommitOutcome.Unknown ? "It may or may not have been done, so look before sending it again." : "It failed, and nothing was done.").ToResult();
            default:
                throw new InvalidOperationException($"A command answered {outcome}.");
        }
    }

    internal static IActionResult TooLarge()
        => new Problem(StatusCodes.Status413PayloadTooLarge, "This is more than this address takes, so nothing was done.").ToResult();

    /// <summary>What a gate's decision that is not an allow answers: a refusal with its reason as any refusal is, otherwise one of three fixed
    /// sentences.</summary>
    internal static IActionResult Denied(HttpContext http, AuthorizeDecision decision) => decision switch
    {
        AuthorizeDecision.Refuse refuse => Refused(http, refuse.Reason),
        AuthorizeDecision.Absent => Answer(http, new Problem(StatusCodes.Status404NotFound, Nothing)),
        AuthorizeDecision.Challenge => Answer(http, new Problem(StatusCodes.Status401Unauthorized, "Sign in to do this.")),
        _ => Answer(http, new Problem(StatusCodes.Status403Forbidden, "This isn't open to you.")),
    };

    /// <summary>A problem as the answer, a <c>401</c> with what the application asks of a caller who is not signed in.</summary>
    private static IActionResult Answer(HttpContext http, Problem problem)
    {
        if (problem.Status == StatusCodes.Status401Unauthorized)
            foreach (var challenge in http.RequestServices.GetServices<IChallenge>())
                http.Response.Headers.Append(HeaderNames.WWWAuthenticate, challenge.For(http));
        return problem.ToResult();
    }

    internal static IActionResult Unbound(ModelStateDictionary state)
        => new Problem(StatusCodes.Status400BadRequest, Errors: new ValidationProblemDetails(state).Errors.AsReadOnly()).ToResult();

    private static void Log(HttpContext http, Exception cause)
        => http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(Answers))
            .LogError(cause, "A command failed at {Path}", http.Request.Path);
}
