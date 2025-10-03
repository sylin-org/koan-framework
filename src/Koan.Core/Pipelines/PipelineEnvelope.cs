using System;
using System.Collections.Generic;

namespace Koan.Core.Pipelines;

/// <summary>
/// Wraps an entity as it moves through the semantic pipeline so enrichment stages can
/// attach features or diagnostics without losing access to the original model.
/// </summary>
/// <typeparam name="TEntity">The entity type carried by the pipeline.</typeparam>
public sealed class PipelineEnvelope<TEntity>
{
    private readonly Dictionary<string, object?> _features;
    private readonly Dictionary<string, object?> _metadata;

    public PipelineEnvelope(TEntity entity)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _features = new(StringComparer.OrdinalIgnoreCase);
        _metadata = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the entity that entered the pipeline. Mutations operate on this instance.
    /// </summary>
    public TEntity Entity { get; }

    /// <summary>
    /// Gets the enrichment feature bag for the envelope.
    /// </summary>
    public IDictionary<string, object?> Features => _features;

    /// <summary>
    /// Gets the diagnostic metadata associated with the envelope.
    /// </summary>
    public IDictionary<string, object?> Metadata => _metadata;

    /// <summary>
    /// Gets the exception captured by the first failing stage, if any.
    /// </summary>
    public Exception? Error { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the envelope has encountered an error.
    /// </summary>
    public bool IsFaulted => Error is not null;

    /// <summary>
    /// Gets a value indicating whether the envelope has been handled by a terminal stage.
    /// </summary>
    public bool IsCompleted { get; private set; }

    /// <summary>
    /// Records an error for the envelope. Only the first error is preserved.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    public void RecordError(Exception exception)
    {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        Error ??= exception;
    }

    /// <summary>
    /// Marks the envelope as completed so subsequent stages in the root pipeline are skipped.
    /// </summary>
    public void Complete() => IsCompleted = true;
}
