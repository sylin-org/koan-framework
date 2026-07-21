namespace Koan.Web.Context;

/// <summary>
/// Contributes validated request-lifetime context after ASP.NET Core authentication and before authorization.
/// Contributors run in ascending <see cref="Order"/>. After each asynchronous contribution completes, Koan enters
/// its pending ambient scopes synchronously before invoking the next contributor.
/// </summary>
public interface IWebContextContributor
{
    /// <summary>Tie-break for dependent context. Lower values contribute first.</summary>
    int Order => 0;

    /// <summary>Resolve and contribute context for the current request.</summary>
    ValueTask ContributeAsync(WebContext context);
}
