using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.AdapterSurface.InMemory.Tests.RelationshipExpansion;

/// <summary>
/// AN-leak — anonymous callers see only <see cref="WorkStatus.Published"/> works. The grant header
/// <c>X-Test-Grant: drafts</c> lifts the wall, modeling a per-caller grant that opens reviewer (Draft)
/// visibility (T2). A work walled here must never surface through a parent's <c>?with=all</c> expansion. It adds the operation it is told
/// to <see cref="Told"/> on the response (WEB-0089).
/// </summary>
public sealed class WorkVisibilityHook : IRequestOptionsHook<Work>
{
    /// <summary>The response header each run adds the operation it was told to.</summary>
    public const string Told = "Work-Told";

    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Work> ctx, QueryOptions opts)
    {
        ctx.Http?.Response.Headers.Append(Told, ctx.Operation.ToString());
        var grant = ctx.Http?.Request.Headers["X-Test-Grant"].ToString();
        if (string.Equals(grant, "drafts", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        opts.AddPredicate<Work>(w => w.Status == WorkStatus.Published);
        return Task.CompletedTask;
    }
}
