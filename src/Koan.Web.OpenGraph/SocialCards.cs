using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Core.Logging;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Web.OpenGraph;

/// <summary>
/// The static entrypoint for declaring social cards. A single fluent <c>For&lt;T&gt;</c> chain registers
/// one card per entity type; the framework injects the resulting head block into the SPA shell on the
/// crawler path. See the proposal at docs/decisions/WEB-0001 for the design rationale.
/// </summary>
public static class SocialCards
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For("Koan.Web.OpenGraph");

    /// <summary>
    /// Declare the card for <typeparamref name="T"/>. The route template's single non-catch-all token
    /// is matched against the request path (trailing slug segments are discarded); the resolver maps
    /// that token to an entity; the returned builder's selectors project the card fields.
    /// </summary>
    public static SocialCardBuilder<T> For<T>(string routeTemplate, Func<string, Task<T?>> resolve)
        where T : Entity<T>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeTemplate);
        ArgumentNullException.ThrowIfNull(resolve);

        var builder = new SocialCardBuilder<T>();
        var registration = new CardRegistration
        {
            TypeDiscriminator = typeof(T).Name,
            Matcher = new RouteTokenMatcher(routeTemplate),
            ResolveAndProject = async token =>
            {
                var entity = await resolve(token).ConfigureAwait(false);
                return entity is null ? null : builder.Project(entity);
            },
            ProjectFromEntity = entity => builder.Project((T)entity),
        };

        SocialCardRegistry.Register(typeof(T), registration);
        WireLifecycle<T>();
        return builder;
    }

    /// <summary>Clears card declarations. Host-owned lifecycle plans need no process reset.</summary>
    public static void Reset() => SocialCardRegistry.Reset();

    private static void WireLifecycle<T>() where T : Entity<T>
    {
        // Warm on upsert: the entity is already in hand, so this costs one write and no read. The
        // handler looks up the current registration at fire time so it survives Reset + re-register.
        Entity<T, string>.Lifecycle.AfterUpsert(ctx =>
        {
            if (!SocialCardRegistry.TryGet(typeof(T), out var registration))
            {
                return ValueTask.CompletedTask;
            }

            return WarmAsync(registration, ctx.Current, ctx.Current.Id);
        });

        // Evict on remove.
        Entity<T, string>.Lifecycle.AfterRemove(ctx =>
        {
            if (!SocialCardRegistry.TryGet(typeof(T), out var registration))
            {
                return ValueTask.CompletedTask;
            }

            return EvictAsync(registration, ctx.Current.Id);
        });
    }

    private static async ValueTask WarmAsync(CardRegistration registration, object entity, string id)
    {
        try
        {
            var card = registration.ProjectFromEntity(entity);
            var snapshot = SocialCardSnapshot.FromCard(registration.KeyFor(id), card);
            await snapshot.Save().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Warming is best-effort: the lazy request-time path rebuilds the snapshot on a miss.
            Log.DataWarning("opengraph.warm", "failed", ("id", id), ("error", ex.Message));
        }
    }

    private static async ValueTask EvictAsync(CardRegistration registration, string id)
    {
        try
        {
            await SocialCardSnapshot.Remove(registration.KeyFor(id)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.DataWarning("opengraph.evict", "failed", ("id", id), ("error", ex.Message));
        }
    }
}
