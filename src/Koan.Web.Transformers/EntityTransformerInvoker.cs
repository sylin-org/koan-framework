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

    public async Task<object?> Parse(Stream body, string contentType, HttpContext httpContext)
    {
        var entity = await _inner.Parse(body, contentType, httpContext);
        return entity;
    }

    public async Task<object> ParseMany(Stream body, string contentType, HttpContext httpContext)
    {
        var entities = await _inner.ParseMany(body, contentType, httpContext);
        return entities;
    }

    public Task<object> Transform(object model, HttpContext httpContext)
    {
        if (model is not TEntity typed)
        {
            throw new InvalidOperationException($"Expected model of type {typeof(TEntity).FullName}, but received {model?.GetType().FullName ?? "null"}.");
        }

        return _inner.Transform(typed, httpContext);
    }

    public async Task<object> TransformMany(IEnumerable models, HttpContext httpContext)
    {
        IEnumerable<TEntity> typed = models as IEnumerable<TEntity> ?? models.Cast<TEntity>().ToArray();

        return await _inner.TransformMany(typed, httpContext);
    }
}
