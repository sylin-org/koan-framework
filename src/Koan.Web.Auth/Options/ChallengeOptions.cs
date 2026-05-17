namespace Koan.Web.Auth.Options;

/// <summary>
/// Configuration for the framework's built-in <c>JsonChallengeHandler</c>, which converts
/// cookie-auth redirects (302 to sign-in / access-denied) into 401 / 403 responses when the
/// request looks like XHR or JSON. Bound from <c>Koan:Web:Auth:Challenge</c>.
/// </summary>
/// <remarks>
/// <para>
/// All three knobs combine with OR semantics: a request is treated as API-shaped when ANY of the
/// configured signals match. Defaults keep the legacy heuristic (<c>Accept: application/json</c>,
/// <c>X-Requested-With: XMLHttpRequest</c>, paths under <c>/api</c> + <c>/.well-known</c> +
/// <c>/me</c>) so existing apps are unchanged.
/// </para>
/// <para>
/// Disable the built-in handler entirely with <see cref="Enabled"/> = false. Apps that want
/// different behavior should ship their own <see cref="Koan.Web.Auth.Flow.IKoanAuthFlowHandler"/>
/// at a lower (earlier) priority and set
/// <see cref="Koan.Web.Auth.Flow.AuthChallengeContext.ResponseHandled"/>.
/// </para>
/// </remarks>
public sealed class ChallengeOptions
{
    public const string SectionPath = "Koan:Web:Auth:Challenge";

    /// <summary>Master switch for the built-in JSON-challenge handler. Default true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Path-prefix list that counts as "API-shaped". Defaults include common Koan routes
    /// (<c>/api</c>, <c>/.well-known</c>, <c>/me</c>) plus the typical platform-specific
    /// surfaces (<c>/account</c>, <c>/v1</c>) that downstream apps need. Each entry is matched
    /// via <c>PathString.StartsWithSegments</c>, so "/api" matches "/api/users" but not
    /// "/api-old". Trailing slashes are normalized.
    /// </summary>
    public IReadOnlyList<string> ApiPaths { get; init; } = new[]
    {
        "/api",
        "/.well-known",
        "/me",
        "/account",
        "/v1",
    };

    /// <summary>Treat <c>Accept: application/json</c> as an API request. Default true.</summary>
    public bool TreatAcceptJsonAsApi { get; init; } = true;

    /// <summary>Treat <c>X-Requested-With: XMLHttpRequest</c> as an API request. Default true.</summary>
    public bool TreatXhrHeaderAsApi { get; init; } = true;
}
