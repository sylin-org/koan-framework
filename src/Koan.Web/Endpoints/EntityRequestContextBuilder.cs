using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;

using Koan.Web.Hooks;

namespace Koan.Web.Endpoints;

/// <summary>
/// Helper for constructing <see cref="EntityRequestContext"/> instances across protocols.
/// </summary>
public sealed class EntityRequestContextBuilder
{
    private readonly IServiceProvider _services;

    public EntityRequestContextBuilder(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public EntityRequestContext Build(QueryOptions options, CancellationToken cancellationToken, HttpContext? httpContext = null, ClaimsPrincipal? user = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        var provider = httpContext?.RequestServices ?? _services;
        var principal = user ?? httpContext?.User ?? new ClaimsPrincipal();
        return new EntityRequestContext(provider, options, cancellationToken, httpContext, principal);
    }
}


