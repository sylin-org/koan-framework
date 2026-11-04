using System.Collections.Frozen;
using System.Security.Claims;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Hosting;

/// <summary>
/// The application's whole request pipeline as the host built it, every middleware of it, which answers a request made in process
/// (ARCH-0144): a tool call, a read by address, a page drawn again, a static copy. The request has a DI scope, <c>Items</c>, features
/// and an execution flow of its own, and is read as the caller of the request in hand or as nobody.
/// </summary>
/// <remarks>It holds the pipeline by decorating ASP.NET's <see cref="IApplicationBuilderFactory"/>: the first component of the host's
/// builder is outermost, so its <c>next</c> is everything a request over HTTP passes through.</remarks>
public sealed class KoanWebPipeline(IServiceProvider services, IHttpContextAccessor accessor) : IApplicationBuilderFactory
{
    /// <summary>What belongs to the request in hand's connection, its body or its own answer, and so never to a request it makes: its hops
    /// and forwarding, which its scheme, host and address already resolved; what describes its body; what it asked of its own answer,
    /// which a conditional or a range would apply to another resource's tag; and its <c>Origin</c> (SEC-0039). Every <c>Content-*</c>
    /// header besides.</summary>
    private static readonly FrozenSet<string> NeverCarried = new[]
    {
        HeaderNames.Host, HeaderNames.Connection, HeaderNames.KeepAlive, KoanWebConstants.Headers.ProxyConnection, HeaderNames.ProxyAuthorization,
        HeaderNames.TE, HeaderNames.Trailer, HeaderNames.TransferEncoding, HeaderNames.Upgrade, HeaderNames.Expect,
        KoanWebConstants.Headers.Forwarded, ForwardedHeadersDefaults.XForwardedForHeaderName, ForwardedHeadersDefaults.XForwardedHostHeaderName,
        ForwardedHeadersDefaults.XForwardedProtoHeaderName, ForwardedHeadersDefaults.XForwardedPrefixHeaderName,
        HeaderNames.Accept, HeaderNames.AcceptEncoding, HeaderNames.IfMatch, HeaderNames.IfNoneMatch, HeaderNames.IfModifiedSince,
        HeaderNames.IfUnmodifiedSince, HeaderNames.IfRange, HeaderNames.Range, HeaderNames.Origin,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private RequestDelegate? _pipeline;

    /// <summary>Answers <paramref name="request"/> as the application answers it over HTTP from the same reader, buffered whole. An exception
    /// nothing in the pipeline handled reaches the caller; a process that serves no web requests answers <c>503</c>.</summary>
    /// <exception cref="ArgumentException">The address is not a path, or the headers given carry a credential.</exception>
    public async Task<InProcessAnswer> Send(InProcessRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Address.StartsWith('/'))
            throw new ArgumentException($"An address is a path and query under the application's base URL, such as /places; '{request.Address}' is not.", nameof(request));
        if (request.Headers.ContainsKey(HeaderNames.Cookie) || request.Headers.ContainsKey(HeaderNames.Authorization))
            throw new ArgumentException("A request made in process is read as the caller of the request in hand or as nobody, so it carries no credential it is given.", nameof(request));

        // Read when a request is sent: the host resolves its builder factory before the startup filter has built the base URL.
        var baseUrl = services.GetRequiredService<KoanWebBaseUrl>();
        var inHand = accessor.HttpContext;
        // Nobody takes nothing of the request in hand but its scheme and host.
        var carried = request.AsNobody ? null : inHand;
        var origin = inHand is null && baseUrl.Current is { } current ? new Uri(current) : null;
        await using var scope = services.CreateAsyncScope();
        using var answer = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = ct };
        if (carried is not null)
            foreach (var (name, value) in carried.Request.Headers)
                if (Carried(name, request.Withheld)) http.Request.Headers[name] = value;
        foreach (var (name, value) in request.Headers) http.Request.Headers[name] = value;

        var query = request.Address.IndexOf('?');
        http.Request.Method = request.Method;
        // With no connection there is no scheme to go by; a request from this process is as safe as one on TLS.
        http.Request.Scheme = inHand?.Request.Scheme ?? origin?.Scheme ?? Uri.UriSchemeHttps;
        http.Request.Host = inHand?.Request.Host ?? (origin is null ? new HostString("localhost") : new HostString(origin.Authority));
        http.Request.Path = PathString.FromUriComponent((inHand?.Request.PathBase ?? baseUrl.PathBase).ToUriComponent() + (query < 0 ? request.Address : request.Address[..query]));
        http.Request.QueryString = query < 0 ? QueryString.Empty : new QueryString(request.Address[query..]);
        if (!request.Body.IsEmpty || http.Request.ContentType is { Length: > 0 })
        {
            http.Request.ContentLength = request.Body.Length;
            http.Request.Body = new MemoryStream(request.Body.ToArray(), writable: false);
        }
        http.Response.Body = answer;
        http.Connection.RemoteIpAddress = carried?.Connection.RemoteIpAddress;

        // The caller is the request in hand's own credential, which authentication reads; with none in hand, the principal given and its tier.
        http.User = inHand is null && !request.AsNobody ? request.Principal ?? new ClaimsPrincipal() : new ClaimsPrincipal();
        foreach (var (type, feature) in request.Features) http.Features[type] = feature;
        http.Features.Set(InProcessMark.Instance);
        // With a connection, the origin is the connection's, as on any request; with none, the principal's.
        http.Features.Set(inHand is null && OriginStamp.TierOf(http.User) is { } tier ? new OriginFeature(tier) : null);

        var serve = _pipeline ?? Unserved;
        Task served;
        using (ExecutionContext.SuppressFlow())
            served = Task.Run(async () =>
            {
                accessor.HttpContext = http;
                try { await serve(http); }
                finally { accessor.HttpContext = null; }
            }, CancellationToken.None);
        await served.ConfigureAwait(false);
        return new InProcessAnswer(http.Response.StatusCode, http.Response.Headers, answer.ToArray());
    }

    IApplicationBuilder IApplicationBuilderFactory.CreateBuilder(IFeatureCollection serverFeatures)
    {
        var builder = new ApplicationBuilderFactory(services).CreateBuilder(serverFeatures);
        builder.Use(next => _pipeline = next);
        return builder;
    }

    private static bool Carried(string name, ISet<string> withheld)
        => !name.StartsWith(KoanWebConstants.Headers.ContentPrefix, StringComparison.OrdinalIgnoreCase) && !NeverCarried.Contains(name) && !withheld.Contains(name);

    /// <summary>What a process that serves no web requests answers.</summary>
    private static Task Unserved(HttpContext http)
        => new Problem(StatusCodes.Status503ServiceUnavailable, "This process serves no requests.").ToResult()
            .ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
}
