using Koan.Web.Hooks;

namespace Koan.Web.AdapterSurface.InMemory.Tests.RelationshipExpansion;

/// <summary>
/// AN-leak — anonymous callers never see a <see cref="Maker.Secret"/> maker. <c>X-Test-Role: admin</c>
/// lifts the wall. The same WEB-0068 predicate seam the collection/get-by-id paths use must apply to a
/// parent resolved through relationship expansion.
/// </summary>
public sealed class MakerVisibilityHook : IRequestOptionsHook<Maker>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<Maker> ctx, QueryOptions opts)
    {
        var role = ctx.Http?.Request.Headers["X-Test-Role"].ToString();
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        opts.AddPredicate<Maker>(m => !m.Secret);
        return Task.CompletedTask;
    }
}
