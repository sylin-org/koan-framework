namespace Koan.Identity.Tenancy;

/// <summary>
/// SEC-0007 P4 — app-level configuration for the tenant-resolution carriers (bound from
/// <c>Koan:Tenancy:Resolution</c>). The per-tenant routing handle is <c>TenantRecord.Code</c>; this configures
/// <i>how</i> each carrier reads the inbound signal. All four carriers run (in registration order: claim → header →
/// subdomain → path) and the first to yield a candidate wins; the middleware then membership-authorizes it.
/// </summary>
public sealed class TenancyResolutionOptions
{
    /// <summary>The config section these options bind from.</summary>
    public const string SectionPath = global::Koan.Tenancy.TenancyOptions.SectionPath + ":Resolution";

    /// <summary>The claim type the <c>claim</c> carrier reads (a tenant id minted at sign-in). Default <c>tenant</c>.</summary>
    public string ClaimType { get; set; } = "tenant";

    /// <summary>The request header the <c>header</c> carrier reads (a tenant id or <c>TenantRecord.Code</c>). Default <c>X-Koan-Tenant</c>.</summary>
    public string HeaderName { get; set; } = "X-Koan-Tenant";

    /// <summary>
    /// The base hosts the <c>subdomain</c> carrier strips to read the leading label as a <c>TenantRecord.Code</c>
    /// (e.g. base <c>app.example.com</c> + host <c>acme.app.example.com</c> → code <c>acme</c>). Empty = the
    /// subdomain carrier is inert (it cannot know which suffix is the app vs. the tenant label).
    /// </summary>
    public IList<string> BaseHosts { get; set; } = new List<string>();

    /// <summary>The path prefix the <c>path</c> carrier reads the next segment after as a <c>TenantRecord.Code</c> (e.g. <c>/t/acme/…</c> → <c>acme</c>). Default <c>/t/</c>.</summary>
    public string PathPrefix { get; set; } = "/t/";

}
