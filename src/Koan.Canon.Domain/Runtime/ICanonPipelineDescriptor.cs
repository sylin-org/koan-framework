using System;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Common abstraction for pipeline descriptors regardless of the canonical entity type.
/// </summary>
public interface ICanonPipelineDescriptor
{
    /// <summary>
    /// CLR type of the canonical entity associated with the descriptor.
    /// </summary>
    Type ModelType { get; }

    /// <summary>
    /// Metadata describing the configured pipeline.
    /// </summary>
    CanonPipelineMetadata Metadata { get; }
}
