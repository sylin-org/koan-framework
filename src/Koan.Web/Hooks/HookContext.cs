using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Abstractions;
using Koan.Web.Endpoints;
using System.Linq;
using System.Security.Claims;

namespace Koan.Web.Hooks;

/// <summary>
/// Per-request context available to all hooks.
/// </summary>
public sealed class HookContext<TEntity>
{
    public HookContext(EntityRequestContext requestContext)
    {
        Request = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
    }

    public EntityRequestContext Request { get; }

    public HttpContext? Http => Request.HttpContext;

    public IServiceProvider Services => Request.Services;

    public QueryOptions Options => Request.Options;

    public IQueryCapabilities Capabilities => Request.Capabilities;

    public IDictionary<string, string> ResponseHeaders => Request.Headers;

    public ClaimsPrincipal User => Request.User;

    public CancellationToken Ct => Request.CancellationToken;

    public void Warn(string msg) => Request.Warn(msg);

    public IReadOnlyList<string> Warnings => Request.Warnings as IReadOnlyList<string> ?? Request.Warnings.ToArray();

    private IActionResult? _shortCircuit;

    public void ShortCircuit(IActionResult result) => _shortCircuit = result;

    public bool IsShortCircuited => _shortCircuit is not null;

    public IActionResult? ShortCircuitResult => _shortCircuit;
}

