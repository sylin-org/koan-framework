using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

internal sealed class EntityEnricherInvoker<TEntity>(IEntityEnricher<TEntity> inner) : IEntityEnricherInvoker
{
    public Type EntityType => typeof(TEntity);

    public bool ShouldActivate(HttpContext context)
        => inner is not ITransformerActivationPredicate gate || gate.ShouldActivate(context);

    public async Task<object> Enrich(object model, HttpContext context)
    {
        if (model is not TEntity typed)
        {
            throw new InvalidOperationException(
                $"Expected enricher input of type {typeof(TEntity).FullName}, but received {model?.GetType().FullName ?? "null"}.");
        }

        var result = await inner.Enrich(typed, context);
        return result!;
    }

    public async Task<object> EnrichMany(IEnumerable models, HttpContext context)
    {
        IReadOnlyList<TEntity> typed = models switch
        {
            IReadOnlyList<TEntity> ro => ro,
            IList<TEntity> list => (IReadOnlyList<TEntity>)new List<TEntity>(list),
            IEnumerable<TEntity> seq => seq.ToArray(),
            _ => models.Cast<TEntity>().ToArray()
        };

        var result = await inner.EnrichMany(typed, context);
        return result;
    }
}
