using Koan.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Koan.Web.Filters;

/// <summary>
/// A declared entity write that came as a form and was refused answers its <see cref="Problem"/> with what the form sent as
/// <see cref="Problem.Sent"/> (WEB-0088), so the page that renders the refusal shows the form again as it was filled. A body that could not
/// bind is refused as a problem too, with each field it could not take named, as every other refusal of the write is.
/// </summary>
internal sealed class KeptForm : IAsyncAlwaysRunResultFilter
{
    public static readonly KeptForm Instance = new();

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.HasFormContentType && context.Result is ObjectResult { StatusCode: >= 400 } refused
            && (refused.Value as Problem ?? (refused.Value is ValidationProblemDetails && !context.ModelState.IsValid
                ? (Answers.Unbound(context.ModelState) as ObjectResult)?.Value as Problem
                : null)) is { } problem)
        {
            try
            {
                var form = await request.ReadFormAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
                context.Result = (problem with { Sent = SentForm.Fields(form) }).ToResult();
            }
            catch (Exception unread) when (unread is InvalidDataException or BadHttpRequestException)
            {
                // A form past its limits, or one that cannot be read, is refused as it is, with nothing of it kept.
            }
        }

        await next().ConfigureAwait(false);
    }
}
