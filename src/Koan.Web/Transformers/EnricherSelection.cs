using System;

namespace Koan.Web.Transformers;

/// <summary>
/// Resolved <see cref="IEntityEnricher{TEntity}"/> ready to be invoked on a response. Returned as
/// part of <see cref="TransformerOutputSelection"/> alongside an optional terminal transformer.
/// </summary>
public sealed class EnricherSelection
{
    internal EnricherSelection(Type entityType, IEntityEnricherInvoker invoker)
    {
        EntityType = entityType;
        Invoker = invoker;
    }

    public Type EntityType { get; }

    internal IEntityEnricherInvoker Invoker { get; }
}
