using Koan.Data.Core.Lifecycle;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Example.Approvals.Foundation.Web;

public sealed class ApprovalExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not EntityLifecycleCancelledException rejected) return;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "This request cannot be changed",
            Detail = rejected.Message
        };
        problem.Extensions["code"] = rejected.ReasonCode;
        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        context.ExceptionHandled = true;
    }
}
