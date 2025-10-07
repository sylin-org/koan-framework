using System;

namespace Koan.Web.Transformers;

public interface ITransformerRegistry
{
    void Register<TEntity, TShape>(IEntityTransformer<TEntity, TShape> transformer, string[] contentTypes, int priority = (int)TransformerPriority.Discovered);

    TransformerSelection? ResolveForOutput(Type entityType, IEnumerable<string> acceptTypes);

    TransformerSelection? ResolveForInput(Type entityType, string contentType);

    IReadOnlyList<string> GetContentTypes(Type entityType);

    TransformerSelection? ResolveForOutput<TEntity>(IEnumerable<string> acceptTypes)
        => ResolveForOutput(typeof(TEntity), acceptTypes);

    TransformerSelection? ResolveForInput<TEntity>(string contentType)
        => ResolveForInput(typeof(TEntity), contentType);

    IReadOnlyList<string> GetContentTypes<TEntity>()
        => GetContentTypes(typeof(TEntity));
}