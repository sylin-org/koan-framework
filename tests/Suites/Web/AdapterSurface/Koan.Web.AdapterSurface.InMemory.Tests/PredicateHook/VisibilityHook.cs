using Koan.Web.Hooks;

namespace Koan.Web.AdapterSurface.InMemory.Tests.PredicateHook;

/// <summary>
/// Test-only hook exercising WEB-0068. Reads two custom request headers to make the test
/// matrix easy: <c>X-Test-Role</c> (admin = no filter beyond "not Hidden"; otherwise the
/// caller only sees Published or own-Draft) and <c>X-Test-User</c> (caller id for the
/// own-Draft branch).
/// </summary>
public sealed class VisibilityHook : IRequestOptionsHook<VisibilityWidget>
{
    public int Order => 0;

    public Task OnBuildingOptions(HookContext<VisibilityWidget> ctx, QueryOptions opts)
    {
        // Hidden is never visible to anyone (mirrors the gposingway Merged semantics).
        opts.AddPredicate<VisibilityWidget>(w => w.Status != VisibilityStatus.Hidden);

        var role = ctx.Http?.Request.Headers["X-Test-Role"].ToString();
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var userId = ctx.Http?.Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrEmpty(userId))
        {
            // Anonymous: only Published.
            opts.AddPredicate<VisibilityWidget>(w => w.Status == VisibilityStatus.Published);
            return Task.CompletedTask;
        }

        // Authenticated user: Published OR own Draft.
        opts.AddPredicate<VisibilityWidget>(w =>
            w.Status == VisibilityStatus.Published || w.OwnerId == userId);

        return Task.CompletedTask;
    }
}
