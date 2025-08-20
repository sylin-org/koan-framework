using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sora.Data.Abstractions;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Web.Hooks;

/// <summary>
/// High-level action type used by authorization hooks.
/// </summary>
public enum ActionType { Read, Write, Remove }
/// <summary>
/// Whether an action applies to a collection or a single model.
/// </summary>
public enum ActionScope { Collection, Model }

/// <summary>
/// Query and shaping options flowing through the controller and hooks.
/// </summary>
public sealed class QueryOptions
{
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = Sora.Web.Infrastructure.SoraWebConstants.Defaults.DefaultPageSize;
    public List<SortSpec> Sort { get; set; } = new();
    public string Shape { get; set; } = "full"; // full | map | dict
    public string? View { get; set; }
    public Dictionary<string, string> Extras { get; } = new();
}

/// <summary>
/// Field-based sort specification.
/// </summary>
public sealed record SortSpec(string Field, bool Desc);

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

/// <summary>
/// Authorization request shape passed to IAuthorizeHook.
/// </summary>
public sealed class AuthorizeRequest
{
    public string Method { get; init; } = "GET";
    public ActionType Action { get; init; }
    public ActionScope Scope { get; init; }
    public string? Id { get; init; }
}

/// <summary>
/// Authorization decision result.
/// </summary>
public abstract record AuthorizeDecision
{
    public sealed record Allow() : AuthorizeDecision;
    public sealed record Forbid(string? Reason = null) : AuthorizeDecision;
    public sealed record Challenge() : AuthorizeDecision;
    public static Allow Allowed() => new();
    public static Forbid Forbidden(string? reason = null) => new(reason);
    public static Challenge Challenged() => new();
}

/// <summary>
/// Emission decision allows replacing or continuing the payload pipeline.
/// </summary>
public abstract record EmitDecision
{
    public sealed record Continue() : EmitDecision;
    public sealed record Replace(object Payload) : EmitDecision;
    public static Continue Next() => new();
    public static Replace With(object payload) => new(payload);
}

/// <summary>
/// Hooks can opt into ordering to control execution precedence.
/// </summary>
public interface IOrderedHook { int Order { get; } }

/// <summary>
/// Authorization hook for allow/forbid/challenge decisions.
/// </summary>
public interface IAuthorizeHook<TEntity> : IOrderedHook
{
    Task<AuthorizeDecision> OnAuthorizeAsync(HookContext<TEntity> ctx, AuthorizeRequest req);
}

/// <summary>
/// Hook invoked while building query options from the request.
/// </summary>
public interface IRequestOptionsHook<TEntity> : IOrderedHook
{
    Task OnBuildingOptionsAsync(HookContext<TEntity> ctx, QueryOptions opts);
}

/// <summary>
/// Collection-level lifecycle hooks.
/// </summary>
public interface ICollectionHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetchAsync(HookContext<TEntity> ctx, QueryOptions opts);
    Task OnAfterFetchAsync(HookContext<TEntity> ctx, List<TEntity> items);
}

/// <summary>
/// Model-level lifecycle hooks.
/// </summary>
public interface IModelHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetchAsync(HookContext<TEntity> ctx, string id);
    Task OnAfterFetchAsync(HookContext<TEntity> ctx, TEntity? model);
    Task OnBeforeSaveAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnAfterSaveAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnBeforeDeleteAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnAfterDeleteAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnBeforePatchAsync(HookContext<TEntity> ctx, string id, object patch);
    Task OnAfterPatchAsync(HookContext<TEntity> ctx, TEntity model);
}

/// <summary>
/// Emission hooks can transform or replace payloads before the response.
/// </summary>
public interface IEmitHook<TEntity> : IOrderedHook
{
    Task<EmitDecision> OnEmitCollectionAsync(HookContext<TEntity> ctx, object payload);
    Task<EmitDecision> OnEmitModelAsync(HookContext<TEntity> ctx, object payload);
}
