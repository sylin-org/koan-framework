using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;

namespace Koan.Identity.Roles;

internal static class ScopedRoleMutationGuard
{
    private static readonly AsyncLocal<Permit?> Current = new();
    private const string Code = "scoped-role.mutation.guard";

    public static IDisposable Allow<TEntity>(string id, EntityLifecycleOperation operation,
        Func<CancellationToken, ValueTask> revalidate)
        where TEntity : Entity<TEntity>
    {
        if (Current.Value is not null)
            throw new InvalidOperationException("Scoped-role mutation permits cannot be nested.");
        var permit = new Permit(typeof(TEntity), ScopedRoleScopeRef.Require(id, nameof(id)), operation,
            revalidate ?? throw new ArgumentNullException(nameof(revalidate)));
        Current.Value = permit;
        return new Exit(permit);
    }

    public static void Register()
    {
        Guard<ScopedRoleScope>();
        Guard<ScopedRoleDefinition>();
        Guard<ScopedRoleBinding>();
        Guard<ScopedRolePolicy>();
    }

    private static void Guard<TEntity>() where TEntity : Entity<TEntity>
    {
        Entity<TEntity>.Lifecycle.BeforeUpsert(Demand);
        Entity<TEntity>.Lifecycle.BeforeRemove(Demand);
    }

    private static async ValueTask<EntityLifecycleResult> Demand<TEntity>(EntityLifecycleContext<TEntity> context)
        where TEntity : class
    {
        var permit = Current.Value;
        var id = (context.Current as Koan.Data.Abstractions.IEntity<string>)?.Id;
        if (permit is not null && !permit.Consumed && permit.EntityType == typeof(TEntity) &&
            permit.Operation == context.Operation && StringComparer.Ordinal.Equals(permit.Id, id))
        {
            permit.Consumed = true; // a re-entrant hook cannot reuse this authorization for another write.
            await permit.Revalidate(context.CancellationToken).ConfigureAwait(false);
            return context.Proceed();
        }
        return context.Cancel(
            $"{typeof(TEntity).Name} is managed by RoleEngine; generic entity mutation is not authorized.", Code);
    }

    private sealed class Exit : IDisposable
    {
        private readonly Permit _permit;
        private bool _disposed;
        public Exit(Permit permit) => _permit = permit;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(Current.Value, _permit)) Current.Value = null;
        }
    }

    private sealed record Permit(Type EntityType, string Id, EntityLifecycleOperation Operation,
        Func<CancellationToken, ValueTask> Revalidate)
    {
        public bool Consumed { get; set; }
    }
}
