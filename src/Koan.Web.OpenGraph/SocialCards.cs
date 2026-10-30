using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Composition;
using Koan.Data.Core.Model;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.OpenGraph;

/// <summary>
/// The static entrypoint for declaring social cards. A single fluent <c>For&lt;T&gt;</c> chain registers
/// one card per entity type; the framework injects the resulting head block into the SPA shell on the
/// crawler path. See the proposal at docs/decisions/WEB-0001 for the design rationale.
/// </summary>
public static class SocialCards
{
    /// <summary>
    /// Declare the card for <typeparamref name="T"/>. The route template's single non-catch-all token
    /// is matched against the request path (trailing slug segments are discarded); the resolver maps
    /// that token to an entity, or to null where the application shows no card for it; the Entity's read gate and
    /// read constraint then decide for the request's reader (WEB-0083); the returned builder's selectors project the
    /// card fields. A token the reader may not see receives the default card, as an unknown one does.
    /// </summary>
    public static SocialCardBuilder<T> For<T>(string routeTemplate, Func<string, Task<T?>> resolve)
        where T : Entity<T>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeTemplate);
        ArgumentNullException.ThrowIfNull(resolve);

        var services = KoanCompositionScope.RequireServices($"SocialCards.For<{typeof(T).Name}>");
        var registry = SocialCardRegistry.GetOrCreate(services);

        var builder = new SocialCardBuilder<T>();
        registry.Register(typeof(T), new CardRegistration
        {
            Matcher = new RouteTokenMatcher(routeTemplate),
            CardFor = async (token, http, ct) =>
            {
                var entity = await resolve(token).ConfigureAwait(false);
                if (entity is null) return null;

                var readable = await ReadFor<T>(entity.Id, http, ct).ConfigureAwait(false);
                return readable is null ? null : builder.Project(readable);
            },
        });
        return builder;
    }

    // The one read every surface shares: the same gate, constraint and hooks GET {id} answers for this reader.
    private static async Task<T?> ReadFor<T>(string id, HttpContext http, CancellationToken ct)
        where T : Entity<T>
    {
        var services = http.RequestServices;
        var context = services.GetRequiredService<EntityRequestContextBuilder>().Build(new QueryOptions(), ct, http);
        var read = await services.GetRequiredService<IEntityEndpointService<T, string>>()
            .GetById(new EntityGetByIdRequest<string> { Context = context, Id = id })
            .ConfigureAwait(false);
        return read.IsShortCircuited ? null : read.Model;
    }
}
