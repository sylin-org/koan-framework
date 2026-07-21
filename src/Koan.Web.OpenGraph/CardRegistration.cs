using System.Threading.Tasks;

namespace Koan.Web.OpenGraph;

/// <summary>
/// One type's card registration, type-erased so the request-time middleware (which holds a path,
/// not a <c>T</c>) can match and project without knowing the entity type.
/// </summary>
internal sealed class CardRegistration
{
    public required string TypeDiscriminator { get; init; }

    public required RouteTokenMatcher Matcher { get; init; }

    /// <summary>Lazy path: resolve the token to an entity, then project. Null when the entity is not found.</summary>
    public required Func<string, Task<SocialCard?>> ResolveAndProject { get; init; }

    /// <summary>Warm path: project an already-loaded entity (handed to us by Lifecycle).</summary>
    public required Func<object, SocialCard> ProjectFromEntity { get; init; }

    public string KeyFor(string token) => $"{TypeDiscriminator}:{token}";
}
