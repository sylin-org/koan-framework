using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

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

    public IReadOnlyDictionary<string, string> Headers { get; }

    public IReadOnlyList<string> Warnings { get; }
}

public sealed class EntityCollectionResult<TEntity> : EntityEndpointResult
{
    public EntityCollectionResult(EntityRequestContext context, IReadOnlyList<TEntity> items, int totalCount, object? payload, object? shortCircuit = null)
        : base(context, payload, shortCircuit)
    {
        Items = items;
        TotalCount = totalCount;
    }

    public IReadOnlyList<TEntity> Items { get; }

    public int TotalCount { get; }
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
