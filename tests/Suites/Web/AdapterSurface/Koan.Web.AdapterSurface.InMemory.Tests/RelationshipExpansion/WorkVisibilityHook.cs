using Koan.Web.Hooks;

namespace Koan.Web.AdapterSurface.InMemory.Tests.RelationshipExpansion;

/// <summary>
/// AN-leak — anonymous callers see only <see cref="WorkStatus.Published"/> works. The grant header
/// <c>X-Test-Grant: drafts</c> lifts the wall, modeling a per-caller grant that opens reviewer (Draft)
/// visibility (T2). A work walled here must never surface through a parent's <c>?with=all</c> expansion.
/// </summary>
public sealed class WorkVisibilityHook : IRequestOptionsHook<Work>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Work> ctx, QueryOptions opts)
    {
        var grant = ctx.Http?.Request.Headers["X-Test-Grant"].ToString();
        if (string.Equals(grant, "drafts", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        opts.AddPredicate<Work>(w => w.Status == WorkStatus.Published);
        return Task.CompletedTask;
    }
}
