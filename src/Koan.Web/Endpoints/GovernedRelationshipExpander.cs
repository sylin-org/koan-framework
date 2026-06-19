using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Web.Filtering;
using Koan.Web.Hooks;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Endpoints;

/// <summary>
/// AN-leak (docs/assessment/09 §10) — governed relationship expansion for the agent/HTTP read path.
///
/// The raw <c>Entity&lt;T,K&gt;.GetRelatives()</c> loaders are app-authority: they fetch related rows
/// via <c>Data&lt;TChild,TKey&gt;.All()</c> and filter by foreign key in-memory, with no request
/// predicates. That is correct when service code traverses relationships, but a row-level visibility
/// bypass on the agent/HTTP read path — a caller reads a visible parent, expands with <c>?with=all</c>,
/// and receives related rows a direct query of that type would hide (WEB-0068).
///
/// This expander resolves each edge as a <em>governed</em> query through the related type's own
/// visibility pipeline: it runs the related type's <see cref="IRequestOptionsHook{TEntity}"/>s for the
/// same request (principal + headers), AND-composes the contributed predicates with the foreign-key
/// filter, and pushes the whole thing down to the adapter. An edge inherits its resolved query's
/// projection (DECIDED #1); a related row hidden by predicate produces no count, no field name, no
/// existence signal (walled-means-silent, §9.4). The fix lives entirely in <c>Koan.Web</c> — the
/// domain traversal API in <c>Koan.Data.Core</c> stays app-authority and unchanged (DECIDED #3).
///
/// MCP rides the same <see cref="IEntityEndpointService{TEntity,TKey}"/>, so fixing the endpoint fixes
/// every transport — the governance is not duplicated per transport (the AN3 lesson).
/// </summary>
internal static class GovernedRelationshipExpander
{
    private static readonly MethodInfo ResolveChildrenMethod =
        typeof(GovernedRelationshipExpander).GetMethod(nameof(ResolveChildren), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ResolveParentMethod =
        typeof(GovernedRelationshipExpander).GetMethod(nameof(ResolveParent), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static async Task<RelationshipGraph<TEntity>> ExpandAsync<TEntity, TKey>(
        TEntity entity,
        TKey entityId,
        EntityRequestContext context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var graph = new RelationshipGraph<TEntity> { Entity = entity };
        var services = context.Services;
        var metadata = services.GetRequiredService<IRelationshipMetadata>();
        var ct = context.CancellationToken;

        // Parents: a relationship foreign key on the root resolves to the parent type, gated by the
        // parent type's own visibility predicates. A null FK keeps the existing present-with-null shape
        // (the relationship simply isn't set); a walled or missing parent is omitted — the FK scalar
        // already lives on the root row, so omission discloses nothing new (T-parent, walled-means-silent).
        foreach (var (propertyName, parentType) in metadata.GetParentRelationships(typeof(TEntity)))
        {
            var foreignKey = ReadForeignKey(entity!, propertyName);
            if (foreignKey is null)
            {
                graph.Parents[propertyName] = null;
                continue;
            }

            var task = (Task<object?>)ResolveParentMethod
                .MakeGenericMethod(parentType, typeof(TKey))
                .Invoke(null, new object?[] { services, context, foreignKey, ct })!;
            var parent = await task;
            if (parent is not null)
            {
                graph.Parents[propertyName] = parent;
            }
        }

        // Children: each (referenceProperty, childType) edge resolves to a governed query of the child
        // type filtered by FK == root id, AND-composed with the child's visibility predicates and pushed
        // down to the adapter (this also fixes the All()+in-memory N-load). An edge that resolves to zero
        // visible rows is omitted entirely — no empty-but-present edge that would leak the relationship.
        foreach (var (referenceProperty, childType) in metadata.GetChildRelationships(typeof(TEntity)))
        {
            var task = (Task<IReadOnlyList<object>>)ResolveChildrenMethod
                .MakeGenericMethod(childType, typeof(TKey))
                .Invoke(null, new object?[] { services, context, referenceProperty, entityId, ct })!;
            var rows = await task;
            if (rows.Count == 0)
            {
                continue;
            }

            var childTypeName = childType.Name;
            if (!graph.Children.TryGetValue(childTypeName, out var byProperty))
            {
                byProperty = new Dictionary<string, IReadOnlyList<object>>();
                graph.Children[childTypeName] = byProperty;
            }

            byProperty[referenceProperty] = rows;
        }

        return graph;
    }

    private static async Task<IReadOnlyList<object>> ResolveChildren<TChild, TKey>(
        IServiceProvider services,
        EntityRequestContext rootContext,
        string referenceProperty,
        TKey parentId,
        CancellationToken ct)
        where TChild : class, IEntity<TKey>
        where TKey : notnull
    {
        var predicates = await ResolveVisibilityPredicates<TChild>(services, rootContext, ct);
        if (predicates is null)
        {
            // The child type's options pipeline short-circuited (denied) — the whole edge is walled.
            return Array.Empty<object>();
        }

        var foreignKeyFilter = BuildForeignKeyFilter<TChild>(referenceProperty, parentId);
        var composed = QueryFilterComposer.AndAll<TChild>(foreignKeyFilter, predicates);

        var rows = await Data<TChild, TKey>.All(QueryDefinition.All.Where(composed), ct);
        return rows.Cast<object>().ToList();
    }

    private static async Task<object?> ResolveParent<TParent, TKey>(
        IServiceProvider services,
        EntityRequestContext rootContext,
        TKey parentId,
        CancellationToken ct)
        where TParent : class, IEntity<TKey>
        where TKey : notnull
    {
        var predicates = await ResolveVisibilityPredicates<TParent>(services, rootContext, ct);
        if (predicates is null)
        {
            return null;
        }

        var parent = await Data<TParent, TKey>.Get(parentId, ct);
        if (parent is null)
        {
            return null;
        }

        // Mirror EntityEndpointService.GetById: a parent failing any of its type's visibility predicates
        // is walled — return null so the edge is omitted (the same NotFound-equivalence the keyed read
        // uses). Compiling and invoking the literal predicate is the ground truth of intent for a gate.
        foreach (var predicate in predicates)
        {
            var typed = predicate as Expression<Func<TParent, bool>>
                ?? throw new InvalidOperationException(
                    $"QueryOptions.Predicates entry was {predicate?.GetType().FullName ?? "null"}, expected " +
                    $"Expression<Func<{typeof(TParent).FullName}, bool>>. Use QueryOptions.AddPredicate<TParent>(...).");
            if (!typed.Compile().Invoke(parent))
            {
                return null;
            }
        }

        return parent;
    }

    /// <summary>
    /// Runs the related type's <see cref="IRequestOptionsHook{TEntity}"/>s for THIS request (same
    /// principal + headers) so its WEB-0068 visibility predicates are produced. Returns <c>null</c> when
    /// the options pipeline short-circuits (a denial) — the edge is then treated as fully walled.
    /// </summary>
    private static async Task<IReadOnlyList<LambdaExpression>?> ResolveVisibilityPredicates<TRelated>(
        IServiceProvider services,
        EntityRequestContext rootContext,
        CancellationToken ct)
        where TRelated : class
    {
        var options = new QueryOptions();
        var relatedContext = new EntityRequestContext(
            services, options, ct, rootContext.HttpContext, rootContext.User);

        var pipeline = services.GetRequiredService<IEntityHookPipeline<TRelated>>();
        var hookContext = pipeline.CreateContext(relatedContext);

        var allowed = await pipeline.BuildOptions(hookContext, options);
        return allowed ? options.Predicates : null;
    }

    private static Filter BuildForeignKeyFilter<TChild>(string referenceProperty, object parentId)
    {
        var property = typeof(TChild).GetProperty(referenceProperty, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Relationship reference property '{referenceProperty}' was not found on {typeof(TChild).FullName}.");

        var parameter = Expression.Parameter(typeof(TChild), "c");
        var member = Expression.Property(parameter, property);
        var constant = Expression.Constant(parentId, property.PropertyType);
        var body = Expression.Equal(member, constant);
        var lambda = Expression.Lambda<Func<TChild, bool>>(body, parameter);

        return LinqFilterCompiler.Compile(lambda);
    }

    private static object? ReadForeignKey(object entity, string propertyName)
    {
        var property = entity.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(entity);
    }
}
