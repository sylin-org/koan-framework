using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Koan.Web.Filters;

/// <summary>Keeps a declared create's body readable after it is bound, so the endpoint can see which members it named (WEB-0087).</summary>
internal sealed class BufferedBody : IResourceFilter
{
    public static readonly BufferedBody Instance = new();

    public void OnResourceExecuting(ResourceExecutingContext context) => context.HttpContext.Request.EnableBuffering();

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
