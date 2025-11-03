using System;
using System.Collections;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

internal sealed class EntityTransformerInvoker<TEntity, TShape> : IEntityTransformerInvoker
{
    private readonly IEntityTransformer<TEntity, TShape> _inner;

    public EntityTransformerInvoker(IEntityTransformer<TEntity, TShape> inner)
    {
        _inner = inner;
    }

    public Type EntityType => typeof(TEntity);

    public async Task<object?> ParseAsync(Stream body, string contentType, HttpContext httpContext)
    {
        var entity = await _inner.ParseAsync(body, contentType, httpContext);
        return entity;
    }

    public async Task<object> ParseManyAsync(Stream body, string contentType, HttpContext httpContext)
    {
        var entities = await _inner.ParseManyAsync(body, contentType, httpContext);
        return entities;
    }

    public Task<object> TransformAsync(object model, HttpContext httpContext)
    {
        if (model is not TEntity typed)
        {
            throw new InvalidOperationException($"Expected model of type {typeof(TEntity).FullName}, but received {model?.GetType().FullName ?? "null"}.");
        }

        return _inner.TransformAsync(typed, httpContext);
    }

    public async Task<object> TransformManyAsync(IEnumerable models, HttpContext httpContext)
    {
        IEnumerable<TEntity> typed = models as IEnumerable<TEntity> ?? models.Cast<TEntity>().ToArray();

        return await _inner.TransformManyAsync(typed, httpContext);
    }
}
