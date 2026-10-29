namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0028 — a request waiting for a person's decision, as the application's consent page renders it: who asks, for
/// what, and where the page's Allow and Deny forms post. Found by <see cref="ConsentPage.Find"/>.
/// </summary>
/// <param name="Id">What approve and deny address: the code flow's <c>rid</c>, or the device's user code normalized.</param>
/// <param name="ClientId">The requesting client's <c>client_id</c>.</param>
/// <param name="ClientName">The name the client registered, or its <c>client_id</c> when it has none. Untrusted display
/// text: a dynamically registered client chose it; render it escaped and unverified.</param>
/// <param name="Scopes">The scopes requested.</param>
/// <param name="Resource">The resource the token will be issued for (RFC 8707).</param>
/// <param name="UserCode">The Device flow's user code as a device shows it (<c>BCDF-GHJK</c>); null for the code flow.</param>
/// <param name="RedirectUri">Where a code-flow client returns; null for the Device flow.</param>
/// <param name="Approve">The path, under the application's path base, the Allow form posts to.</param>
/// <param name="Deny">The path, under the application's path base, the Deny form posts to.</param>
public sealed record PendingConsent(
    string Id,
    string ClientId,
    string ClientName,
    IReadOnlyList<string> Scopes,
    string Resource,
    string? UserCode,
    string? RedirectUri,
    string Approve,
    string Deny);
