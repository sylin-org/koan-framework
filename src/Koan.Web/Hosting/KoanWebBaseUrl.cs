using System.Net;
using System.Threading;
using Koan.Core;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Hosting;

/// <summary>
/// The one canonical base URL of a Koan web application: scheme, host and optional path, configured once as
/// <c>Koan:Web:ApplicationUrl</c> (WEB-0078).
/// </summary>
/// <remarks>
/// <para>When configured it is authoritative. <see cref="KoanWebStartupFilter"/> serves the application under its
/// path and nothing outside it, and every pillar that writes an absolute URL — the authorization server's issuer,
/// the MCP resource identity, OpenGraph cards, the atproto client metadata — derives from it instead of from the
/// request <c>Host</c> header, which a caller controls.</para>
/// <para>When it is not configured, <see cref="Resolve(HttpContext)"/> derives the base from the live request:
/// scheme, host and the request's path base. That is the Development posture. Outside Development the host says so
/// once, and in Production it refuses at the point of need with a correction naming the key — an application that
/// never asks for an absolute URL keeps booting, one that does cannot publish an origin a caller chose
/// (<see cref="KoanEnv.Gate"/>, ARCH-0128).</para>
/// <para>The configured value is held in two forms: <see cref="Configured"/>, the URL string with its path
/// percent-encoded in canonical form (<c>https://www.example.com/my%20space</c>), and <see cref="PathBase"/>, the
/// decoded path the pipeline matches requests against (<c>/my space</c>). Scheme and host are lowercase, the port
/// appears only when it is not the scheme's default, and there is no trailing slash.</para>
/// </remarks>
public sealed class KoanWebBaseUrl
{
    private static readonly KoanMagic RequestDerived = new(
        Capability: "absolute URLs derived from the request Host header",
        Risk: "a caller who controls the Host header controls the issuer, redirect and card URLs this application writes.",
        Remedy: $"set {ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.ApplicationUrl} to the application's public URL");

    private static readonly string Key = $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.ApplicationUrl}";

    private readonly ILogger? _logger;
    private readonly MagicVerdict _verdict;
    private readonly string? _environment;
    private int _noticed;

    /// <summary>
    /// Create the base URL from the configured value. A null or empty value means "derive from each request".
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is not the application's public URL.</exception>
    public KoanWebBaseUrl(string? applicationUrl, IHostEnvironment? environment = null, ILogger<KoanWebBaseUrl>? logger = null)
    {
        _logger = logger;
        _environment = environment?.EnvironmentName;
        _verdict = KoanEnv.Gate.Evaluate(RequestDerived, environment);
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

    /// <summary>The configured base URL, path percent-encoded, or null when the application derives it from each request.</summary>
    public string? Configured { get; }

    /// <summary>The configured base URL's path, decoded and without a trailing slash, or empty when it has none.</summary>
    public PathString PathBase { get; }

    /// <summary>True when <c>Koan:Web:ApplicationUrl</c> is set.</summary>
    public bool IsConfigured => Configured is not null;

    /// <summary>
    /// The base URL every absolute URL for this request is built on: the configured value when there is one, else
    /// the request's scheme, host and path base.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing is configured and the host runs in Production without <c>Koan:AllowMagicInProduction</c>.
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
        if (Configured is not null) return Configured;

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

    // The derived origin is normalised like the configured one: lowercase scheme and host, the port only when it is
    // not the scheme's default, so "koan-browser.test:80" and "koan-browser.test" name the same issuer.
    private static string DerivedOrigin(HttpRequest request)
        => Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var origin)
            ? origin.GetLeftPart(UriPartial.Authority)
            : $"{request.Scheme}://{request.Host}";

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

    private void NoticeDerivedOnce()
    {
        if (_verdict == MagicVerdict.Allowed || _logger is null) return;
        if (Interlocked.Exchange(ref _noticed, 1) != 0) return;

        _logger.LogWarning(
            "Koan is writing {Capability} in environment '{Environment}'. {Risk} {Remedy}.",
            RequestDerived.Capability, _environment ?? KoanEnv.EnvironmentName, RequestDerived.Risk, RequestDerived.Remedy);
    }
}
