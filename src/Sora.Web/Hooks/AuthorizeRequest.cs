namespace Sora.Web.Hooks;

/// <summary>
/// Authorization request shape passed to IAuthorizeHook.
/// </summary>
public sealed class AuthorizeRequest
{
    public string Method { get; init; } = "GET";
    public ActionType Action { get; init; }
    public ActionScope Scope { get; init; }
    public string? Id { get; init; }
}