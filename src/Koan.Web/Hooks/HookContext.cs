using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Koan.Core.Capabilities;
using Koan.Web.Endpoints;

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

    public CapabilitySet Capabilities => Request.Capabilities;

    public IDictionary<string, string> ResponseHeaders => Request.Headers;

    public ClaimsPrincipal User => Request.User;

    public CancellationToken Ct => Request.CancellationToken;

    /// <summary>The row as it stood before this write, read where the write's hold opens, or null for a row that did not exist; set before
    /// <c>OnBeforeSave</c> or <c>OnBeforeDelete</c> of a single write, and null on a read or a batch.</summary>
    public TEntity? Prior => Request.Items.TryGetValue(EntityMutationProbe.BeforeKey, out var prior) && prior is TEntity row ? row : default;

    /// <summary>What the operation took beside the entity's members, as it declared it with <c>[Input&lt;TInput&gt;]</c> (WEB-0087), or null
    /// where it declares no input of that type.</summary>
    public TInput? Input<TInput>() where TInput : class => Request.Input as TInput;

    public void Warn(string msg) => Request.Warn(msg);

    public IReadOnlyList<string> Warnings => Request.Warnings as IReadOnlyList<string> ?? Request.Warnings.ToArray();

    private object? _shortCircuit;

    public void ShortCircuit(IActionResult result)
        => _shortCircuit = result ?? throw new ArgumentNullException(nameof(result));

    public void ShortCircuit(object payload)
        => _shortCircuit = payload ?? throw new ArgumentNullException(nameof(payload));

    public bool IsShortCircuited => _shortCircuit is not null;

    /// <summary>A write a hook stopped with a row goes on as that row's write, so its emit runs.</summary>
    internal void Resume() => _shortCircuit = null;

    public object? ShortCircuitPayload => _shortCircuit;

    public IActionResult? ShortCircuitResult => _shortCircuit as IActionResult;
}
