using Koan.Data.Abstractions.Instructions;

namespace Koan.Web.Options;

/// <summary>
/// Options that control Koan's default web pipeline wiring.
/// Prefer configuring via appsettings or AddKoanWeb().
/// </summary>
public sealed class KoanWebOptions
{
    // If true, the module will automatically hook middleware via an IStartupFilter.
    public bool AutoUse { get; set; } = true;

    /// <summary>
    /// WEB-0078: the application's one public base URL - scheme, host and optional path, e.g.
    /// <c>https://www.example.com/app</c>. When set it is authoritative: the pipeline serves under its path and
    /// nothing outside it, and every absolute URL Koan writes (issuer, MCP resource, OpenGraph card, atproto client
    /// metadata) derives from it. When unset, the URL the application accepted at runtime stands in for it (WEB-0081,
    /// <see cref="Hosting.IApplicationUrlStore"/>), else URLs derive from each request's scheme, host and path base.
    /// Read it through <see cref="Hosting.KoanWebBaseUrl"/> or <see cref="KoanWeb.Urls"/>.
    /// Configure via: Koan:Web:ApplicationUrl (env: Koan__Web__ApplicationUrl).
    /// </summary>
    public string? ApplicationUrl { get; set; }

    /// <summary>
    /// WEB-0078: the reverse proxies whose <c>X-Forwarded-Proto</c>, <c>X-Forwarded-Host</c> and <c>X-Forwarded-For</c>
    /// are believed, as IP addresses or CIDR networks, however many stand in a row: declare each one, or its network. The
    /// first address nobody declared is the client. Empty (the default) honours no forwarded header, and a blank or
    /// whitespace entry declares nothing. A
    /// configured or accepted base URL still decides every absolute URL Koan writes, whatever host a proxy forwards.
    /// Configure via: Koan:Web:TrustedProxies:0=10.0.0.5
    /// </summary>
    public IList<string> TrustedProxies { get; } = new List<string>();

    /// <summary>
    /// WEB-0081: how many distinct origins Koan holds while it observes where requests arrive
    /// (<see cref="Hosting.KoanWebOrigins"/>); a new one beyond that evicts the one seen least often, the least recent
    /// among equals. At least 1.
    /// Configure via: Koan:Web:ObservedOriginLimit (env: Koan__Web__ObservedOriginLimit).
    /// </summary>
    public int ObservedOriginLimit { get; set; } = 32;

    /// <summary>
    /// WEB-0079: the folder the ASP.NET Data Protection key ring persists in, absolute or relative to the content
    /// root. When set, the ring lives there under the application name the folder remembers, so a rebuilt container,
    /// a renamed application and every instance over the same folder read what was protected before; the folder is
    /// created (owner-only on Unix) and proved writable at start, and one that cannot be refuses the start. Unset (the
    /// default) leaves the ring where ASP.NET puts it. An application's own <c>PersistKeysTo…</c> wins over it.
    /// Configure via: Koan:Web:KeyRingDirectory (env: Koan__Web__KeyRingDirectory).
    /// </summary>
    public string? KeyRingDirectory { get; set; }

    /// <summary>
    /// WEB-0079: the application name the key ring in <see cref="KeyRingDirectory"/> is scoped to, pinned. Unset (the
    /// default), the folder remembers the name it was first written under, the application's code, and every later
    /// start reads it back, so renaming the application never orphans its keys. A pinned name the folder does not
    /// hold refuses the start, since every key in it would become unreadable. An application's own
    /// <c>SetApplicationName</c> wins over it.
    /// Configure via: Koan:Web:KeyRingApplicationName (env: Koan__Web__KeyRingApplicationName).
    /// </summary>
    public string? KeyRingApplicationName { get; set; }

    // When set, a very light in-pipeline handler responds to GET or HEAD {HealthPath} with { status: "ok" }.
    public string HealthPath { get; set; } = Infrastructure.KoanWebConstants.Routes.ApiHealth;

    // Opt-in static files wiring. If true, UseDefaultFiles()+UseStaticFiles() will be applied.
    public bool EnableStaticFiles { get; set; } = true;

    // If true, controllers are mapped automatically by the startup filter.
    public bool AutoMapControllers { get; set; } = true;

    // Apply a minimal set of secure response headers via middleware.
    public bool EnableSecureHeaders { get; set; } = true;

    // If true, assumes the app sits behind a reverse proxy (e.g., nginx) that applies
    // security headers. When set, Koan will omit emitting its own security headers
    // to avoid duplicates and potential conflicts.
    // Configure via: Koan:Web:IsProxiedApi=true (env: Koan__Web__IsProxiedApi=true)
    public bool IsProxiedApi { get; set; } = false;

    /// <summary>
    /// SEC-0039: refuse, with 403, an unsafe request (anything but <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and <c>TRACE</c>)
    /// that carries a cookie and that a page of another site sent, at every endpoint, <c>/mcp</c> included. A browser sends a
    /// session cookie whoever asks, and a sibling subdomain is <c>same-site</c> to <c>SameSite=Lax</c>; a request with no
    /// cookie has nothing to borrow and is not judged, so a client posting to Koan's OAuth token endpoint passes.
    /// <c>Sec-Fetch-Site</c> decides when present: <c>same-origin</c> and <c>none</c> pass. Without it, an <c>Origin</c> must
    /// be this request's own scheme and host; <c>Origin: null</c> is refused, which with Koan's <c>Referrer-Policy:
    /// no-referrer</c> happens to a same-origin post only where a browser sends no <c>Sec-Fetch-Site</c>: plain HTTP on an
    /// address that is not loopback. A request with neither header is not a browser's and passes. No Koan sign-in posts
    /// across sites. Not known: which MCP clients can set a <c>Cookie</c> header, and what WebKit before 16.4 sends.
    /// Turn it off only for a front end on another site that calls this application with its session cookie under a
    /// credentialed CORS policy, such as <c>Koan:Mcp:AllowedOrigins</c>; it then answers for what that front end may do.
    /// Default true.
    /// Configure via: Koan:Web:RefuseCrossSiteWrites (env: Koan__Web__RefuseCrossSiteWrites).
    /// </summary>
    public bool RefuseCrossSiteWrites { get; set; } = true;

    // Optional Content-Security-Policy value. If null/empty, CSP is not set.
    // Example safe default for simple static content:
    // "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'"
    public string? ContentSecurityPolicy { get; set; }

    // Controls exposure of the observability snapshot endpoint.
    // Default: false; can be enabled via config. In Development, controller may allow it regardless.
    public bool ExposeObservabilitySnapshot { get; set; } = false;

    // Controls how RFC 7386 (merge-patch) treats nulls on non-nullable value types.
    // Default: map to default(T). Alternative: reject with 422.
    public MergePatchNullPolicy MergePatchNullsForNonNullable { get; set; } = MergePatchNullPolicy.SetDefault;

    // Controls how application/json (partial JSON) treats nulls on reference types.
    // Default: SetNull (null sets property to null). Alternative: Ignore or Disallow.
    public PartialJsonNullPolicy PartialJsonNulls { get; set; } = PartialJsonNullPolicy.SetNull;
}
