namespace Koan.Tenancy;

/// <summary>
/// The transport-neutral inbound signal an <see cref="ITenantResolver"/> resolves a tenant from (ARCH-0099 §1).
/// Deliberately free of any ASP.NET / HTTP type so the resolvers stay unit-testable and <c>Koan.Tenancy</c> takes
/// no web dependency — the web bridge (<c>Koan.Identity.Tenancy</c>) adapts an <c>HttpContext</c> into this and
/// runs the resolvers at the <c>AfterAuthentication</c> pipeline stage. The two accessors (<see cref="Claim"/> /
/// <see cref="Header"/>) are pulled lazily so a resolver only touches the one signal it cares about.
/// </summary>
/// <param name="Host">The request host (no port), e.g. <c>acme.app.example.com</c> — the subdomain carrier reads this.</param>
/// <param name="Path">The request path, e.g. <c>/t/acme/orders</c> — the path carrier reads this.</param>
/// <param name="Subject">The authenticated subject id (the canonical person id post-reconciliation), or null when anonymous.</param>
/// <param name="Claim">Reads a claim value off the authenticated principal by claim type (the claim carrier reads this).</param>
/// <param name="Header">Reads a request header value by name (the header carrier reads this).</param>
public sealed record TenantResolutionRequest(
    string? Host,
    string? Path,
    string? Subject,
    Func<string, string?> Claim,
    Func<string, string?> Header);
