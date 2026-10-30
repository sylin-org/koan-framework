using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// WEB-0071 — OIDC configuration manager for a SELF-HOSTED dev provider whose authority is RELATIVE (the Test IdP's
/// <c>/.testoauth</c>, served by this same app). The maintained handler fetches discovery server-side at the start of
/// the very first challenge. The live request owns the browser-facing issuer and authorization endpoint; the app's
/// bound server address owns discovery, token, userinfo, and JWKS traffic. Keeping those two addresses distinct is
/// what makes the simulator work behind a proxy or in a container, where the browser's <c>localhost</c> or public DNS
/// name is not necessarily reachable from the application process. The public issuer is the application's base
/// URL (<see cref="KoanWebBaseUrl"/>, WEB-0078: <c>Koan:Web:ApplicationUrl</c> when set, else the URL the application
/// accepted (WEB-0081), else the request's scheme, host and path base), read per challenge, and the back-channel
/// discovery request carries the request's path base so it reaches the provider under the path Koan serves.
/// </summary>
internal sealed class RequestHostOidcConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly string _relativeAuthority;
    private readonly IHttpContextAccessor _http;
    private readonly OpenIdConnectOptions _options;
    private readonly Func<string?> _resolveBackchannelBase;
    private readonly KoanWebBaseUrl? _baseUrl;
    private readonly object _gate = new();
    private ConfigurationManager<OpenIdConnectConfiguration>? _inner;
    private string? _builtForBackchannelBase;

    public RequestHostOidcConfigurationManager(
        string relativeAuthority,
        IHttpContextAccessor http,
        OpenIdConnectOptions options,
        Func<string?> resolveBackchannelBase,
        KoanWebBaseUrl? baseUrl = null)
    {
        _relativeAuthority = "/" + relativeAuthority.Trim('/');
        _http = http;
        _options = options;
        _resolveBackchannelBase = resolveBackchannelBase;
        _baseUrl = baseUrl;
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        var request = CurrentRequest();
        var publicBase = ResolvePublicBase(request);
        var pathBase = request.PathBase;
        // The back-channel reaches the provider through the bound address, under the same path base the public
        // request carries, because Koan serves nothing outside it (WEB-0078).
        var backchannelBase = ResolveBackchannelBase(publicBase) + pathBase;
        var discovered = await Inner(backchannelBase).GetConfigurationAsync(cancel).ConfigureAwait(false);
        return Project(discovered, publicBase, pathBase);
    }

    public void RequestRefresh() => _inner?.RequestRefresh();

    private HttpRequest CurrentRequest()
        => _http.HttpContext?.Request
            ?? throw new InvalidOperationException(
                "Koan.Web.Auth: the self-hosted OIDC provider's authority is relative and there is no active request " +
                "to resolve it against. A challenge must originate from an HTTP request.");

    private string ResolvePublicBase(HttpRequest req)
        => _baseUrl?.Resolve(req)
           ?? new Uri($"{req.Scheme}://{req.Host}").GetLeftPart(UriPartial.Authority) + req.PathBase;

    private string ResolveBackchannelBase(string publicBase)
    {
        var resolved = _resolveBackchannelBase()?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        if (Uri.TryCreate(publicBase, UriKind.Absolute, out var publicUri) && publicUri.IsLoopback)
        {
            return publicBase;
        }

        throw new InvalidOperationException(
            $"Koan.Web.Auth cannot reach the self-hosted OIDC provider for public issuer " +
            $"'{publicBase}{_relativeAuthority}'. No internal Kestrel address is available. " +
            "Bind the application to an explicit HTTP(S) address with UseUrls, ASPNETCORE_URLS, or ASPNETCORE_HTTP_PORTS.");
    }

    private ConfigurationManager<OpenIdConnectConfiguration> Inner(string backchannelBase)
    {
        if (_inner is not null && _builtForBackchannelBase == backchannelBase) return _inner;
        lock (_gate)
        {
            if (_inner is not null && _builtForBackchannelBase == backchannelBase) return _inner;
            var metadata = $"{backchannelBase}{_relativeAuthority}/.well-known/openid-configuration";
            _inner = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadata,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(_options.Backchannel) { RequireHttps = _options.RequireHttpsMetadata });
            _builtForBackchannelBase = backchannelBase;
            return _inner;
        }
    }

    private OpenIdConnectConfiguration Project(OpenIdConnectConfiguration discovered, string publicBase, PathString pathBase)
    {
        // Clone the discovered document before projecting the two front-channel values. ConfigurationManager caches
        // its instance, and mutating it would leak one request host into another host-header/proxy context.
        var projected = new OpenIdConnectConfiguration(OpenIdConnectConfiguration.Write(discovered))
        {
            Issuer = $"{publicBase}{_relativeAuthority}",
            AuthorizationEndpoint = ToPublicEndpoint(discovered.AuthorizationEndpoint, publicBase, pathBase),
            JsonWebKeySet = discovered.JsonWebKeySet,
        };
        foreach (var signingKey in discovered.SigningKeys)
        {
            projected.SigningKeys.Add(signingKey);
        }

        return projected;
    }

    // The discovered endpoint sits under the back-channel's path base; the public base already carries it.
    private static string ToPublicEndpoint(string endpoint, string publicBase, PathString pathBase)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return endpoint;
        var relative = uri.PathAndQuery;
        if (pathBase.HasValue && relative.StartsWith(pathBase.Value!, StringComparison.OrdinalIgnoreCase))
            relative = relative[pathBase.Value!.Length..];
        return $"{publicBase}{relative}";
    }
}
