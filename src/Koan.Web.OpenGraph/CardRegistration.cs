using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.OpenGraph;

/// <summary>
/// One type's card registration, type-erased so the request-time middleware (which holds a path,
/// not a <c>T</c>) can match and project without knowing the entity type.
/// </summary>
internal sealed class CardRegistration
{
    public required RouteTokenMatcher Matcher { get; init; }

    /// <summary>
    /// The card the request's reader may see for a route token: the application's resolver, then the Entity's own read
    /// for that reader. Null when the resolver finds nothing or the reader may not read what it found.
    /// </summary>
    public required Func<string, HttpContext, CancellationToken, Task<SocialCard?>> CardFor { get; init; }
}
