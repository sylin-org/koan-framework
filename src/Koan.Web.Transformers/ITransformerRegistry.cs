using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

/// <summary>
/// Registry of Terminal transformers (<see cref="IEntityTransformer{TEntity, TShape}"/>) and
/// Pipeline enrichers (<see cref="IEntityEnricher{TEntity}"/>) keyed by entity type. See
/// WEB-0035 (transformers) and WEB-0067 (enrichers + predicate activation).
/// </summary>
public interface ITransformerRegistry
{
    /// <summary>
    /// Register a Terminal-stage transformer for one or more content types. Multiple transformers
    /// can be registered per entity type; Accept negotiation + priority picks the winner.
    /// </summary>
    void Register<TEntity, TShape>(IEntityTransformer<TEntity, TShape> transformer, string[] contentTypes, int priority = (int)TransformerPriority.Discovered);

    /// <summary>
    /// Register a Pipeline-stage enricher. All activated enrichers run in priority order before
    /// the Terminal transformer (or the default JSON serializer) renders the response.
    /// </summary>
    void RegisterEnricher<TEntity>(IEntityEnricher<TEntity> enricher, int priority = (int)TransformerPriority.Discovered);

    /// <summary>
    /// Resolve enrichers + an optional Terminal transformer for a response. Pipeline entries are
    /// filtered by activation predicate; Terminal selection runs Accept negotiation followed by
    /// predicate filtering.
    /// </summary>
    TransformerOutputSelection ResolveOutput(Type entityType, IEnumerable<string> acceptTypes, HttpContext context);

    /// <summary>
    /// Resolve a Terminal transformer for an incoming request body. Enrichers do not participate
    /// in input parsing. Transformers whose predicate fails are excluded.
    /// </summary>
    TransformerSelection? ResolveForInput(Type entityType, string contentType, HttpContext context);

    /// <summary>
    /// Content types declared by registered Terminal transformers. Used by Swagger/OpenAPI to
    /// advertise media-type variants. Pipeline enrichers do not contribute — they don't change the
    /// wire shape.
    /// </summary>
    IReadOnlyList<string> GetContentTypes(Type entityType);

    TransformerOutputSelection ResolveOutput<TEntity>(IEnumerable<string> acceptTypes, HttpContext context)
        => ResolveOutput(typeof(TEntity), acceptTypes, context);

    TransformerSelection? ResolveForInput<TEntity>(string contentType, HttpContext context)
        => ResolveForInput(typeof(TEntity), contentType, context);

    IReadOnlyList<string> GetContentTypes<TEntity>()
        => GetContentTypes(typeof(TEntity));
}
