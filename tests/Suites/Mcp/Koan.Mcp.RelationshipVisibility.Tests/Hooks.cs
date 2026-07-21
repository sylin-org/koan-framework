using Koan.Web.Hooks;

namespace Koan.Mcp.RelationshipVisibility.Tests;

/// <summary>Anonymous callers never see a Secret maker (same WEB-0068 seam the REST suite uses).</summary>
public sealed class MakerVisibilityHook : IRequestOptionsHook<Maker>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Maker> ctx, QueryOptions opts)
    {
        opts.AddPredicate<Maker>(m => !m.Secret);
        return Task.CompletedTask;
    }
}

/// <summary>Anonymous callers see only Published works — a Draft must never surface through expansion.</summary>
public sealed class WorkVisibilityHook : IRequestOptionsHook<Work>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Work> ctx, QueryOptions opts)
    {
        opts.AddPredicate<Work>(w => w.Status == WorkStatus.Published);
        return Task.CompletedTask;
    }
}
