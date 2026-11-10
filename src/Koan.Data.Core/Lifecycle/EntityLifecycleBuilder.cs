using System.Linq.Expressions;
using Koan.Core.Composition;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Lifecycle;

/// <summary>Host-composed lifecycle declarations for one entity type.</summary>
public sealed class EntityLifecycleBuilder<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    internal EntityLifecycleBuilder() { }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeLoad(
        Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler)
    {
        Plan().AddBeforeLoad(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeLoad(
        Func<EntityLifecycleContext<TEntity>, EntityLifecycleResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return BeforeLoad(ctx => new ValueTask<EntityLifecycleResult>(handler(ctx)));
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterLoad(Func<EntityLifecycleContext<TEntity>, ValueTask> handler)
    {
        Plan().AddAfterLoad(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterLoad(Action<EntityLifecycleContext<TEntity>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AfterLoad(ctx => { handler(ctx); return ValueTask.CompletedTask; });
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeUpsert(
        Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler)
    {
        Plan().AddBeforeUpsert(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeUpsert(
        Func<EntityLifecycleContext<TEntity>, EntityLifecycleResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return BeforeUpsert(ctx => new ValueTask<EntityLifecycleResult>(handler(ctx)));
    }

    /// <summary>
    /// Declares the transitions <paramref name="member"/> takes, each a value it goes from and the value it goes to (DATA-0171), as one
    /// before-upsert rule: a save whose row stood at one value and would stand at another that no pair names is refused with a
    /// <see cref="Refusal"/>, a conflict whose word is <c>transition</c>, in a sentence naming the member, the value it stands at and the
    /// values it goes to from there. A save that leaves the member as it stands passes, and so does one with no row as it stood.
    /// <code>Order.Lifecycle.Transitions(order => order.Status, (Status.Open, Status.Shipped), (Status.Open, Status.Cancelled));</code>
    /// </summary>
    public EntityLifecycleBuilder<TEntity, TKey> Transitions<TState>(
        Expression<Func<TEntity, TState>> member, params (TState From, TState To)[] transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        if (member?.Body is not MemberExpression { Member.Name: var name })
            throw new ArgumentException($"Name the member of {typeof(TEntity).Name} whose transitions these are, as entity => entity.Status.", nameof(member));
        var read = member.Compile();
        var table = transitions.ToArray();
        return BeforeUpsert(context =>
        {
            if (context.Prior is null) return context.Proceed();
            var (from, to) = (read(context.Prior), read(context.Current));
            if (EqualityComparer<TState>.Default.Equals(from, to) || table.Contains((from, to))) return context.Proceed();
            var next = table.Where(pair => EqualityComparer<TState>.Default.Equals(pair.From, from)).Select(pair => $"{pair.To}").ToArray();
            throw new Refusal(Reason.Conflict(Constants.Lifecycle.Transition, $"{name} does not go from {from} to {to}; " +
                (next.Length == 0 ? $"nothing follows {from}." : $"from {from} it goes to {string.Join(" or ", next)}.")));
        });
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterUpsert(Func<EntityLifecycleContext<TEntity>, ValueTask> handler)
    {
        Plan().AddAfterUpsert(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterUpsert(Action<EntityLifecycleContext<TEntity>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AfterUpsert(ctx => { handler(ctx); return ValueTask.CompletedTask; });
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeRemove(
        Func<EntityLifecycleContext<TEntity>, ValueTask<EntityLifecycleResult>> handler)
    {
        Plan().AddBeforeRemove(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> BeforeRemove(
        Func<EntityLifecycleContext<TEntity>, EntityLifecycleResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return BeforeRemove(ctx => new ValueTask<EntityLifecycleResult>(handler(ctx)));
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterRemove(Func<EntityLifecycleContext<TEntity>, ValueTask> handler)
    {
        Plan().AddAfterRemove(handler ?? throw new ArgumentNullException(nameof(handler)));
        return this;
    }

    public EntityLifecycleBuilder<TEntity, TKey> AfterRemove(Action<EntityLifecycleContext<TEntity>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AfterRemove(ctx => { handler(ctx); return ValueTask.CompletedTask; });
    }

    private static EntityLifecyclePlan<TEntity, TKey> Plan()
    {
        var services = KoanCompositionScope.RequireServices($"{typeof(TEntity).Name}.Lifecycle");
        var existing = services
            .Where(d => d.ServiceType == typeof(EntityLifecyclePlan<TEntity, TKey>))
            .Select(d => d.ImplementationInstance)
            .OfType<EntityLifecyclePlan<TEntity, TKey>>()
            .LastOrDefault();
        if (existing is not null) return existing;

        var plan = new EntityLifecyclePlan<TEntity, TKey>();
        services.AddSingleton(plan);
        services.AddSingleton<IEntityLifecyclePlan>(plan);
        return plan;
    }
}
