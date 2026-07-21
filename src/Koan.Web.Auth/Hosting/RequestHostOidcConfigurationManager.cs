using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// WEB-0071 — OIDC configuration manager for a SELF-HOSTED dev provider whose authority is RELATIVE (the Test IdP's
/// <c>/.testoauth</c>, served by this same app). The maintained handler fetches discovery server-side at the start of
/// the very first challenge — before any app code runs — so the absolute metadata address can't be guessed at boot
/// from <c>ASPNETCORE_URLS</c> (a container may not expose it). Resolve it from the LIVE request host at discovery
/// time instead: the Test IdP builds every discovery endpoint (including the browser-facing
/// <c>authorization_endpoint</c>) from the request host, so the back-channel MUST use the public host the browser is
/// on — which also keeps the issuer self-consistent. Forwarded headers make <see cref="HttpRequest.Scheme"/>/<see
/// cref="HttpRequest.Host"/> the external scheme/host behind a proxy.
/// </summary>
internal sealed class RequestHostOidcConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly string _relativeAuthority;
    private readonly IHttpContextAccessor _http;
    private readonly OpenIdConnectOptions _options;
    private readonly object _gate = new();
    private ConfigurationManager<OpenIdConnectConfiguration>? _inner;
    private string? _builtForBase;

    public RequestHostOidcConfigurationManager(string relativeAuthority, IHttpContextAccessor http, OpenIdConnectOptions options)
    {
        _relativeAuthority = "/" + relativeAuthority.Trim('/');
        _http = http;
        _options = options;
    }

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        => Inner(ResolveBase()).GetConfigurationAsync(cancel);

    public void RequestRefresh() => _inner?.RequestRefresh();

    private string ResolveBase()
    {
        var req = _http.HttpContext?.Request
            ?? throw new InvalidOperationException(
                "Koan.Web.Auth: the self-hosted OIDC provider's authority is relative and there is no active request " +
                "to resolve it against. A challenge must originate from an HTTP request.");
        return $"{req.Scheme}://{req.Host}";
    }

    private ConfigurationManager<OpenIdConnectConfiguration> Inner(string baseUrl)
    {
        if (_inner is not null && _builtForBase == baseUrl) return _inner;
        lock (_gate)
        {
            if (_inner is not null && _builtForBase == baseUrl) return _inner;
            var metadata = $"{baseUrl}{_relativeAuthority}/.well-known/openid-configuration";
            _inner = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadata,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(_options.Backchannel) { RequireHttps = _options.RequireHttpsMetadata });
            _builtForBase = baseUrl;
            return _inner;
        }
    }
}
