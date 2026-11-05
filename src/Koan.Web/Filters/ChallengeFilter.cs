using Koan.Web.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Filters;

/// <summary>Every <c>401</c> an action answers carries what the application asks of a caller who is not signed in, each
/// <see cref="IChallenge"/> registered (ARCH-0146), whoever wrote it: a refusal through <c>Answers</c>, a gate's, or a page the
/// application answers <c>401</c> itself. A challenge already written, such as a bearer scheme's, stands alone.</summary>
internal sealed class ChallengeFilter : IAlwaysRunResultFilter
{
    public static readonly ChallengeFilter Instance = new();

    public void OnResultExecuting(ResultExecutingContext context)
    {
        var http = context.HttpContext;
        var status = (context.Result as IStatusCodeActionResult)?.StatusCode ?? http.Response.StatusCode;
        if (status != StatusCodes.Status401Unauthorized || http.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate)) return;
        foreach (var challenge in http.RequestServices.GetServices<IChallenge>())
            http.Response.Headers.Append(HeaderNames.WWWAuthenticate, challenge.For(http));
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
