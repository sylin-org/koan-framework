using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sora.Data.Abstractions;

namespace Sora.Web.Hooks;

/// <summary>
/// Per-request context available to all hooks.
/// </summary>
public sealed class HookContext<TEntity>
{
    public HttpContext Http { get; init; } = default!;
    public IServiceProvider Services { get; init; } = default!;
    public QueryOptions Options { get; init; } = default!;
    public IQueryCapabilities Capabilities { get; init; } = default!;
    public IDictionary<string, string> ResponseHeaders { get; } = new Dictionary<string, string>();
    public ClaimsPrincipal User => Http.User;
    public CancellationToken Ct { get; init; }

    private IActionResult? _shortCircuit;
    public void ShortCircuit(IActionResult result) => _shortCircuit = result;
    public bool IsShortCircuited => _shortCircuit != null;
    public IActionResult? ShortCircuitResult => _shortCircuit;

    private readonly List<string> _warnings = new();
    public void Warn(string msg) => _warnings.Add(msg);
    public IReadOnlyList<string> Warnings => _warnings;
}