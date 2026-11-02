using System.Net;
using System.Threading;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Hosting;

/// <summary>
/// The one canonical base URL of a Koan web application: scheme, host and optional path (WEB-0078). It is configured as
/// <c>Koan:Web:ApplicationUrl</c>, accepted by the application while it runs (WEB-0081), or derived from each request.
/// </summary>
/// <remarks>
/// <para>Precedence is configured, then accepted, then the request. A configured value is authoritative:
/// <see cref="KoanWebStartupFilter"/> serves the application under its path and nothing outside it. An accepted value
/// is an origin the application chose, kept in its own <see cref="IApplicationUrlStore"/>, loaded before the host
/// serves and changed with <see cref="Accept"/>; a stored value this host cannot use is not held, and the base URL
/// behaves as if nothing were accepted until the next accept replaces it. Every pillar that writes an absolute URL — the authorization server's
/// issuer, the MCP resource identity, OpenGraph cards, the atproto client identity — reads it here at use, so an
/// accepted URL takes effect on the next request without a restart, and none of them takes its origin from the
/// request <c>Host</c> header, which a caller controls.</para>
/// <para>When neither is set, <see cref="Resolve(HttpContext)"/> derives the base from the live request: scheme, host
/// and the request's path base. That is the Development posture. Outside Development the host says so once, and in
/// Production it refuses at the point of need with a correction naming the key — an application that never asks for an
/// absolute URL keeps booting, one that does cannot publish an origin a caller chose (<see cref="KoanEnv.Gate"/>,
/// ARCH-0128). An application whose store declares <see cref="IApplicationUrlStore.DerivesUntilAccepted"/> consents to
/// deriving in Production until it accepts a URL; Koan says so once, and the window closes at the first accept.</para>
/// <para>A configured value is held in two forms: <see cref="Configured"/>, the URL string with its path
/// percent-encoded in canonical form (<c>https://www.example.com/my%20space</c>), and <see cref="PathBase"/>, the
/// decoded path the pipeline matches requests against (<c>/my space</c>). Scheme and host are lowercase, the port
/// appears only when it is not the scheme's default, and there is no trailing slash. An accepted value is normalised
/// the same way and carries no path.</para>
/// </remarks>
public sealed class KoanWebBaseUrl
{
    private static readonly KoanMagic RequestDerived = new(
        Capability: "absolute URLs derived from the request Host header",
        Risk: "a caller who controls the Host header controls the issuer, redirect and card URLs this application writes.",
        Remedy: $"set {ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.ApplicationUrl} to the application's public URL");

    private static readonly string Key = $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.ApplicationUrl}";

    private const string UntilAccepted = nameof(IApplicationUrlStore.DerivesUntilAccepted);

    private readonly ILogger? _logger;
    private readonly MagicVerdict _verdict;
    private readonly string? _environment;
    private readonly IApplicationUrlStore? _store;
    private readonly bool _development;
    private readonly bool _derivesUntilAccepted;
    private readonly IKoanRuntimeFactRecorder? _facts;
    private readonly SemaphoreSlim _accepting = new(1, 1);
    private string? _accepted;
    private (string Value, string Problem)? _unusable;
    private int _noticed;
    private readonly Lazy<IReadOnlyList<IApplicationUrlRule>> _rules;

    /// <summary>
    /// Create the base URL from the configured value. A null or empty value means "accepted, else derived from each
    /// request". <paramref name="store"/> is the application's <see cref="IApplicationUrlStore"/>, when it supplies one;
    /// <paramref name="facts"/> receives the base URL fact again whenever the accepted URL changes;
    /// <paramref name="rules"/> answers what the capabilities active in this host require of an accepted URL, asked at
    /// first use because a rule may depend on what depends on this.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is not the application's public URL.</exception>
    public KoanWebBaseUrl(
        string? applicationUrl,
        IHostEnvironment? environment = null,
        ILogger<KoanWebBaseUrl>? logger = null,
        IApplicationUrlStore? store = null,
        IKoanRuntimeFactRecorder? facts = null,
        Func<IEnumerable<IApplicationUrlRule>>? rules = null)
    {
        _rules = new Lazy<IReadOnlyList<IApplicationUrlRule>>(() => rules?.Invoke().ToList() ?? []);
        _logger = logger;
        _environment = environment?.EnvironmentName;
        _store = store;
        _facts = facts;
        // WEB-0081: an accepted origin is https, or http on a loopback host only where loopback clients exist at all —
        // Development, the one environment the AT Protocol's localhost client profile is allowed in.
        _development = KoanEnv.Gate.DevelopmentOnly(environment);
        // WEB-0081: the store's DerivesUntilAccepted is the capability's own consent (ARCH-0128). It only matters while
        // nothing is configured or accepted, because Resolve answers either of those before it derives.
        _derivesUntilAccepted = store?.DerivesUntilAccepted == true;
        _verdict = KoanEnv.Gate.Evaluate(RequestDerived with { Consent = _derivesUntilAccepted }, environment);
        if (string.IsNullOrWhiteSpace(applicationUrl))
        {
            PathBase = PathString.Empty;
            return;
        }

        if (!TryParse(applicationUrl, out var normalized, out var pathBase, out var problem))
        {
            throw new InvalidOperationException(
                $"{Key} must be the application's public URL: an absolute http or https URL with an optional path and " +
                $"nothing after it, such as https://www.example.com or https://www.example.com/app. It is '{applicationUrl}': {problem}.");
        }

        Configured = normalized;
        PathBase = pathBase;
    }

    /// <summary>The configured base URL, path percent-encoded, or null when <c>Koan:Web:ApplicationUrl</c> is unset.</summary>
    public string? Configured { get; }

    /// <summary>
    /// The origin the application accepted as its base URL (<see cref="Accept"/>), normalised, or null when it accepted
    /// none. A configured value still wins over it.
    /// </summary>
    public string? Accepted => Volatile.Read(ref _accepted);

    /// <summary>
    /// The base URL in force for every request: <see cref="Configured"/>, else <see cref="Accepted"/>; null when each
    /// request's own is derived. Outside a request this is the only base URL there is.
    /// </summary>
    public string? Current => Configured ?? Accepted;

    /// <summary>The configured base URL's path, decoded and without a trailing slash, or empty when it has none.</summary>
    public PathString PathBase { get; }

    /// <summary>True when <c>Koan:Web:ApplicationUrl</c> is set.</summary>
    public bool IsConfigured => Configured is not null;

    /// <summary>
    /// True when nothing is configured or accepted and this environment lets Koan derive the base URL from each request
    /// (<see cref="Resolve(HttpContext)"/> answers rather than refusing).
    /// </summary>
    public bool DerivesFromRequests => Current is null && _verdict != MagicVerdict.Refused;

    /// <summary>
    /// True when the application supplies an <see cref="IApplicationUrlStore"/>, so <see cref="Accept"/> can keep a URL
    /// and one may be read at start. Composition runs before module start, so a consumer that checks the base URL
    /// while the host composes cannot yet see what the store holds.
    /// </summary>
    public bool CanAccept => _store is not null;

    /// <summary>
    /// The base URL every absolute URL for this request is built on: the configured value when there is one, else the
    /// accepted one, else the request's scheme, host and path base.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing is configured or accepted and the host runs in Production without <c>Koan:AllowMagicInProduction</c> or
    /// the application's <see cref="IApplicationUrlStore.DerivesUntilAccepted"/>.
    /// </exception>
    public string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Resolve(context.Request);
    }

    /// <inheritdoc cref="Resolve(HttpContext)"/>
    public string Resolve(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Current is { } current) return current;

        if (_verdict == MagicVerdict.Refused)
        {
            throw new InvalidOperationException(
                $"Koan refuses {RequestDerived.Capability} in Production. {RequestDerived.Risk} " +
                $"To allow it: {RequestDerived.Remedy}, or set {Koan.Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction}=true " +
                "to accept this class of risk framework-wide.");
        }

        NoticeDerivedOnce();
        return DerivedOrigin(request) + request.PathBase;
    }

    /// <summary>
    /// SEC-0037 — the base URL a token's audience is held to or stamped with: <see cref="Current"/>, and in Development, where
    /// a request is the developer's own, the request's. Null otherwise: a caller who chooses the <c>Host</c> would choose
    /// which audience passes, so a token cannot be for this application until something has named it.
    /// </summary>
    public string? Audience(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Current ?? (_development ? Resolve(context.Request) : null);
    }

    /// <summary>
    /// Accept <paramref name="applicationUrl"/> as the application's base URL from now on: it is validated as
    /// <c>Koan:Web:ApplicationUrl</c> is, kept through the application's <see cref="IApplicationUrlStore"/>, and then
    /// answered by <see cref="Resolve(HttpContext)"/> for every request, whatever its <c>Host</c>, with no restart. An
    /// accepted URL is an origin — scheme, host and port — because the path base is mounted when the host starts. It is
    /// https, or http on a loopback host in Development: an issuer must be https (OAuth 2.1) and an AT Protocol client
    /// is https, or the localhost profile in Development only, so any other origin would break sign-in at once or refuse
    /// the next start. A capability active in this host may refuse one too (<see cref="IApplicationUrlRule"/>). A
    /// configured value still wins over it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The application supplies no <see cref="IApplicationUrlStore"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="applicationUrl"/> is not a public origin.</exception>
    public async Task Accept(string applicationUrl, CancellationToken ct = default)
    {
        if (_store is null)
        {
            throw new InvalidOperationException(
                $"Koan keeps an accepted application URL only in the application's own {nameof(IApplicationUrlStore)}, so that " +
                $"it survives a restart. Register one, or set {Key}.");
        }

        if (!TryParseAccepted(applicationUrl, out var normalized, out var problem))
        {
            throw new ArgumentException(
                "An accepted application URL must be the application's public origin, an https URL such as " +
                $"https://www.example.com, validated as {Key} is. It is '{applicationUrl}': {problem}.",
                nameof(applicationUrl));
        }

        await _accepting.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.Save(normalized!, ct).ConfigureAwait(false);
            Volatile.Write(ref _accepted, normalized);
            _unusable = null;
            Restate();
        }
        finally
        {
            _accepting.Release();
        }

        if (Configured is null)
        {
            _logger?.LogInformation(
                "The application accepted {ApplicationUrl} as its base URL; every absolute URL Koan writes derives from it.",
                normalized);
        }
        else
        {
            _logger?.LogInformation(
                "The application accepted {ApplicationUrl} as its base URL; {Key} is configured as {Configured} and still wins.",
                normalized, Key, Configured);
        }
    }

    /// <summary>
    /// Whether <see cref="Accept"/> would take <paramref name="applicationUrl"/>, and why not when it would not, so an
    /// application offers its operator only what it can keep.
    /// </summary>
    public bool WouldAccept(string? applicationUrl, out string problem)
    {
        if (_store is not null) return TryParseAccepted(applicationUrl, out _, out problem);
        problem = $"the application supplies no {nameof(IApplicationUrlStore)} to keep it";
        return false;
    }

    /// <summary>
    /// Read the accepted URL from the application's store, once, before the host serves (<c>WebModule.Start</c>). A
    /// stored value this host cannot use — one that is not a public origin here, such as http on loopback outside
    /// Development — is not held: the base URL behaves as if nothing were accepted, Koan says so once when nothing is
    /// configured, and the store keeps the value until the application's next <see cref="Accept"/> replaces it.
    /// </summary>
    internal async Task Load(CancellationToken ct)
    {
        if (_store is null) return;
        var stored = await _store.Load(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(stored)) return;

        if (TryParseAccepted(stored, out var normalized, out var problem))
        {
            Volatile.Write(ref _accepted, normalized);
            Restate();
            return;
        }

        // WEB-0081: a place moved between environments carries a value that was valid where it was accepted; moving it
        // must not need a database edit. A configured URL wins anyway, so there the value plays no part.
        _unusable = (stored, problem);
        if (Configured is not null) return;
        Restate();
        _logger?.LogWarning(
            "{Store} holds '{Stored}' as the application's accepted URL, and Koan does not use it here: {Problem}. Until " +
            "the application accepts another (KoanWebBaseUrl.Accept), which replaces it, or {Key} is set, {Consequence}. " +
            "Koan leaves the stored value as it is.",
            _store.GetType().FullName, stored, problem, Key,
            DerivesFromRequests
                ? "absolute URLs derive from each request"
                : "an absolute URL is refused where it is needed, as with nothing accepted");
    }

    /// <summary>
    /// The <c>koan.web.baseurl</c> fact (WEB-0078): which base URL is in force and where it comes from. Composition
    /// states it at start; <see cref="Load"/> and <see cref="Accept"/> state it again when the accepted URL changes.
    /// </summary>
    internal (string Summary, string Reason) Statement()
    {
        var store = _store?.GetType().FullName;
        if (Configured is not null)
        {
            return ($"The application's base URL is {Configured} ({Key}); absolute URLs derive from it" +
                    (PathBase.HasValue ? $", and a request outside {PathBase} answers 404." : "."),
                KoanWebConstants.BaseUrl.ConfiguredFactReason);
        }

        if (Accepted is { } accepted)
        {
            return ($"The application's base URL is {accepted}, the URL it accepted ({store}); absolute URLs derive from it, " +
                    "and the application may accept another while it runs.",
                KoanWebConstants.BaseUrl.AcceptedFactReason);
        }

        var source = store is null ? string.Empty : $" ({store})";
        var window = _derivesUntilAccepted
            ? $" until the application accepts one ({UntilAccepted}), in Production too"
            : string.Empty;
        var unusable = _unusable is { } held
            ? $" The store holds '{held.Value}', which is not used: {held.Problem}; the next accepted URL replaces it."
            : string.Empty;
        return ($"No base URL is configured ({Key}) or accepted{source}; absolute URLs derive from each request's scheme, " +
                $"host and path base{window}.{unusable}",
            KoanWebConstants.BaseUrl.DerivedFactReason);
    }

    private void Restate()
    {
        if (_facts is null) return;
        var (summary, reason) = Statement();
        _facts.Record(new KoanFactDescriptor(
            KoanWebConstants.BaseUrl.FactCode,
            KoanFactKind.Discovery,
            KoanFactState.Observed,
            KoanWebConstants.BaseUrl.FactSubject,
            summary,
            reason,
            null,
            typeof(Initialization.WebModule).FullName ?? nameof(Initialization.WebModule),
            KoanWebConstants.BaseUrl.FactSubject));
    }

    // The derived origin is normalised like the configured one: lowercase scheme and host, the port only when it is
    // not the scheme's default, so "koan-browser.test:80" and "koan-browser.test" name the same issuer.
    private static string DerivedOrigin(HttpRequest request)
        => OriginOf(request) ?? $"{request.Scheme}://{request.Host}";

    /// <summary>
    /// The origin <paramref name="request"/> arrived on — its scheme and <c>Host</c>, as the trusted-proxy posture left
    /// them — normalised like a base URL, or null when they form no absolute URL. One rule for the derived base URL and
    /// for the origins <see cref="KoanWebOrigins"/> observes.
    /// </summary>
    internal static string? OriginOf(HttpRequest request)
        => request.Host.HasValue && Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var origin)
            ? origin.GetLeftPart(UriPartial.Authority)
            : null;

    /// <summary>
    /// Normalise a candidate base URL the way <see cref="Configured"/> is held. False when it is not the
    /// application's public URL: not absolute http(s), a query, fragment or user information, an encoded path
    /// separator, or an unspecified listen address such as <c>0.0.0.0</c> or <c>::</c>.
    /// </summary>
    public static bool TryNormalize(string? value, out string? normalized)
        => TryParse(value, out normalized, out _, out _);

    internal static bool TryParse(string? value, out string? normalized, out PathString pathBase, out string problem)
    {
        normalized = null;
        pathBase = PathString.Empty;
        problem = "it is not an absolute http or https URL";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
        {
            problem = "it carries a query, a fragment or user information";
            return false;
        }
        if ((uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            && IPAddress.TryParse(uri.DnsSafeHost, out var address)
            && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
        {
            problem = "an unspecified listen address (0.0.0.0, ::, +, *) is where the server binds, not where the public reaches it";
            return false;
        }
        if (uri.AbsolutePath.Contains("%2F", StringComparison.OrdinalIgnoreCase))
        {
            problem = "its path contains an encoded separator (%2F), which has no single request path";
            return false;
        }

        var escapedPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
        var path = escapedPath.Length == 0 ? string.Empty : "/" + escapedPath;
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
        normalized = $"{uri.Scheme}://{uri.Host}{port}{path}";
        pathBase = path.Length == 0 ? PathString.Empty : new PathString(Uri.UnescapeDataString(path));
        problem = string.Empty;
        return true;
    }

    // An accepted URL is validated as a configured one is, and is an origin: the path base is mounted when the host
    // starts (KoanWebStartupFilter), so a path can only be configured. It is https, or http on a loopback host in
    // Development: OAuth 2.1 requires an https issuer, and the AT Protocol accepts an http client only as its localhost
    // profile, in Development. Any other origin would break sign-in once accepted and refuse the next start, and so would
    // one a capability active in this host cannot use, which that capability says (IApplicationUrlRule).
    private bool TryParseAccepted(string? value, out string? normalized, out string problem)
    {
        if (!TryParse(value, out normalized, out var pathBase, out problem)) return false;
        if (pathBase.HasValue)
        {
            normalized = null;
            problem = $"it carries a path, and an accepted URL is an origin (scheme, host and port); a path is mounted when the " +
                      $"host starts, so it is configured as {Key}";
            return false;
        }

        var origin = new Uri(normalized!);
        if (origin.Scheme != Uri.UriSchemeHttps)
        {
            if (!origin.IsLoopback)
            {
                normalized = null;
                problem = "it is http on a host other than loopback; an accepted URL is https, as OAuth 2.1 requires of an issuer " +
                          "and the AT Protocol of a client, or http on a loopback host in Development";
                return false;
            }
            if (!_development)
            {
                normalized = null;
                problem = $"it is http on a loopback host outside Development, in environment '{_environment ?? KoanEnv.EnvironmentName}'; " +
                          "an http origin is accepted only on a loopback host in Development, where the AT Protocol's localhost " +
                          "client profile exists, so here it must be https";
                return false;
            }
        }

        foreach (var rule in _rules.Value)
        {
            if (rule.Refuses(normalized!) is not { } refused) continue;
            normalized = null;
            problem = refused;
            return false;
        }
        return true;
    }

    private void NoticeDerivedOnce()
    {
        if (_verdict == MagicVerdict.Allowed || _logger is null) return;
        if (Interlocked.Exchange(ref _noticed, 1) != 0) return;

        if (_derivesUntilAccepted)
        {
            // WEB-0081: the application's own posture, named, with how it ends.
            _logger.LogWarning(
                "Koan is writing {Capability} in environment '{Environment}' under the application's posture " +
                "{Store}.{Posture}: nothing is configured or accepted yet. {Risk} It ends the moment the application " +
                "accepts its URL (KoanWebBaseUrl.Accept) or {Key} is set; from then on every absolute URL is that URL.",
                RequestDerived.Capability, _environment ?? KoanEnv.EnvironmentName, _store!.GetType().FullName,
                UntilAccepted, RequestDerived.Risk, Key);
            return;
        }

        _logger.LogWarning(
            "Koan is writing {Capability} in environment '{Environment}'. {Risk} {Remedy}.",
            RequestDerived.Capability, _environment ?? KoanEnv.EnvironmentName, RequestDerived.Risk, RequestDerived.Remedy);
    }
}
