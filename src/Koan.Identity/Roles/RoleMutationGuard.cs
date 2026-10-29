using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;

namespace Koan.Identity.Roles;

/// <summary>Refuses every write to a <see cref="Role"/> or a <see cref="PermissionOverwrite"/> that
/// <see cref="RoleCollection"/> did not permit for that row and operation.</summary>
internal static class RoleMutationGuard
{
    private static readonly AsyncLocal<Permit?> Current = new();
    public static IDisposable Allow<TEntity>(string id, EntityLifecycleOperation operation)
    {
        if (Current.Value is not null) throw new InvalidOperationException("Role mutation permits cannot be nested.");
        var permit = new Permit(typeof(TEntity), RoleContract.Require(id, nameof(id)), operation); Current.Value = permit; return new Exit(permit);
    }
    public static void Register()
    {
        Entity<Role>.Lifecycle.BeforeUpsert(context => Demand(context));
        Entity<Role>.Lifecycle.BeforeRemove(context => Demand(context));
        Entity<PermissionOverwrite>.Lifecycle.BeforeUpsert(context => Demand(context));
        Entity<PermissionOverwrite>.Lifecycle.BeforeRemove(context => Demand(context));
    }
    private static ValueTask<EntityLifecycleResult> Demand<TEntity>(EntityLifecycleContext<TEntity> context) where TEntity : Entity<TEntity>
    {
        var p = Current.Value;
        if (p is not null && !p.Consumed && p.Entity == typeof(TEntity) && p.Operation == context.Operation && StringComparer.Ordinal.Equals(p.Id, context.Current.Id))
        { p.Consumed = true; return ValueTask.FromResult(context.Proceed()); }
        return ValueTask.FromResult(context.Cancel($"{typeof(TEntity).Name} is managed by RoleCollection; generic entity mutation is not authorized.", "role.mutation.guard"));
    }
    private sealed class Exit(Permit permit) : IDisposable { public void Dispose() { if (ReferenceEquals(Current.Value, permit)) Current.Value = null; } }
    private sealed record Permit(Type Entity, string Id, EntityLifecycleOperation Operation) { public bool Consumed { get; set; } }
}
