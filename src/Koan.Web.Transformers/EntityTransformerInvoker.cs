using System;
using System.Collections;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

internal sealed class EntityTransformerInvoker<TEntity, TShape>(IEntityTransformer<TEntity, TShape> inner) : IEntityTransformerInvoker
{

    public Type EntityType => typeof(TEntity);

    public async Task<object?> Parse(Stream body, string contentType, HttpContext httpContext)
    {
        var entity = await inner.Parse(body, contentType, httpContext);
        return entity;
    }

    public async Task<object> ParseMany(Stream body, string contentType, HttpContext httpContext)
    {
        var entities = await inner.ParseMany(body, contentType, httpContext);
        return entities;
    }

    public Task<object> Transform(object model, HttpContext httpContext)
    {
        if (model is not TEntity typed)
        {
            throw new InvalidOperationException($"Expected model of type {typeof(TEntity).FullName}, but received {model?.GetType().FullName ?? "null"}.");
        }

        return inner.Transform(typed, httpContext);
    }

    public async Task<object> TransformMany(IEnumerable models, HttpContext httpContext)
    {
        IEnumerable<TEntity> typed = models as IEnumerable<TEntity> ?? models.Cast<TEntity>().ToArray();

        return await inner.TransformMany(typed, httpContext);
    }
}
