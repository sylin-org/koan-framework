using System.Threading.Tasks;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Koan.Context.Filters;

/// <summary>
/// Ensures EntityContext operations run against the global partition for Koan.Context endpoints.
/// </summary>
public sealed class PartitionScopeFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        using var scope = EntityContext.With(partition: null);
        await next();
    }
}
