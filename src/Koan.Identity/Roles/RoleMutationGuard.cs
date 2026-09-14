using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;

namespace Koan.Identity.Roles;

internal static class RoleMutationGuard
{
    private static readonly AsyncLocal<Permit?> Current = new();
    public static IDisposable Allow(string id, EntityLifecycleOperation operation)
    {
        if (Current.Value is not null) throw new InvalidOperationException("Role mutation permits cannot be nested.");
        var permit = new Permit(RoleContract.Require(id, nameof(id)), operation); Current.Value = permit; return new Exit(permit);
    }
    public static void Register()
    {
        Entity<Role>.Lifecycle.BeforeUpsert(Demand);
        Entity<Role>.Lifecycle.BeforeRemove(Demand);
    }
    private static ValueTask<EntityLifecycleResult> Demand(EntityLifecycleContext<Role> context)
    {
        var p = Current.Value;
        if (p is not null && !p.Consumed && p.Operation == context.Operation && StringComparer.Ordinal.Equals(p.Id, context.Current.Id))
        { p.Consumed = true; return ValueTask.FromResult(context.Proceed()); }
        return ValueTask.FromResult(context.Cancel("Role is managed by RoleCollection; generic entity mutation is not authorized.", "role.mutation.guard"));
    }
    private sealed class Exit(Permit permit) : IDisposable { public void Dispose() { if (ReferenceEquals(Current.Value, permit)) Current.Value = null; } }
    private sealed record Permit(string Id, EntityLifecycleOperation Operation) { public bool Consumed { get; set; } }
}
