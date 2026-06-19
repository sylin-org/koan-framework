using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Hooks;

namespace Koan.Web.Endpoints;

public class EntityEndpointResult
{
    public EntityEndpointResult(EntityRequestContext context, object? payload, object? shortCircuit = null)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Payload = payload;
        ShortCircuitPayload = shortCircuit;
        Headers = new Dictionary<string, string>(context.Headers, StringComparer.OrdinalIgnoreCase);
        Warnings = context.Warnings.ToArray();
    }

    public EntityRequestContext Context { get; }

    public object? Payload { get; }

    public object? ShortCircuitPayload { get; }

    public IActionResult? ShortCircuitResult => ShortCircuitPayload as IActionResult;

    public bool IsShortCircuited => ShortCircuitPayload is not null;

    /// <summary>
    /// ARCH-0092 (§D): when the shared <c>IEntityEndpointService</c> authorize gate denies an operation, the
    /// transport-agnostic <see cref="AuthorizeDecision"/> (Forbid / Challenge) is carried here. Each surface
    /// translates it — REST → 403 / 401, the MCP edge → an access-denied error — instead of an HTTP-specific
    /// result leaking across surfaces.
    /// </summary>
    public AuthorizeDecision? DeniedDecision => ShortCircuitPayload as AuthorizeDecision;

    public IReadOnlyDictionary<string, string> Headers { get; }

    public IReadOnlyList<string> Warnings { get; }
}

public sealed class EntityCollectionResult<TEntity> : EntityEndpointResult
{
    public EntityCollectionResult(EntityRequestContext context, IReadOnlyList<TEntity> items, long totalCount, object? payload, object? shortCircuit = null)
        : base(context, payload, shortCircuit)
    {
        Items = items;
        TotalCount = totalCount;
    }

    public IReadOnlyList<TEntity> Items { get; }

    public long TotalCount { get; }
}

public sealed class EntityModelResult<TEntity> : EntityEndpointResult
{
    public EntityModelResult(EntityRequestContext context, TEntity? model, object? payload, object? shortCircuit = null)
        : base(context, payload, shortCircuit)
    {
        Model = model;
    }

    public TEntity? Model { get; }
}
