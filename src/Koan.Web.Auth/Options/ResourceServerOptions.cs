namespace Koan.Web.Auth.Options;

/// <summary>
/// SEC-0025 — whether this application accepts its own bearer tokens on its ordinary requests, as an OAuth resource
/// server. Configure via: Koan:Web:Auth:ResourceServer.
/// </summary>
public sealed class ResourceServerOptions
{
    /// <summary>
    /// When true, a request that carries an <c>Authorization</c> header is authenticated by its bearer alone, never by the
    /// session cookie: the token is accepted only when its audience is <see cref="Resource"/>, its subject becomes the
    /// person's <c>ClaimTypes.NameIdentifier</c>, and a token that is not accepted answers 401 with
    /// <c>WWW-Authenticate: Bearer error="invalid_token"</c>. Every other request keeps the session cookie. False, the
    /// default, leaves authentication as the cookie scheme alone.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The resource identifier a token's audience must name, with or without one trailing slash; an absolute URI. Null,
    /// the default, is the application's base URL (<c>Koan:Web:ApplicationUrl</c>, WEB-0078, or the URL the application
    /// accepted, WEB-0081), the resource the embedded
    /// authorization server's clients name when they act for a person on this application's pages. Outside Development,
    /// until one of those exists, no token is for this application: a request's <c>Host</c> names nothing (SEC-0037).
    /// </summary>
    public string? Resource { get; init; }
}
