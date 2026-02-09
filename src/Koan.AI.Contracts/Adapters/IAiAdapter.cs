using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Base identity contract for all AI provider adapters (e.g., Ollama, OpenAI).
/// Must be stateless and safe for concurrent use.
///
/// Capability is declared structurally: adapters implement <see cref="IChatAdapter"/>,
/// <see cref="IEmbedAdapter"/>, and/or <see cref="IOcrAdapter"/> to advertise what they support.
/// </summary>
public interface IAiAdapter
{
    /// <summary>Stable identifier for the adapter instance (e.g., "ollama:local:11434").</summary>
    string Id { get; }

    /// <summary>Human-friendly name, for logs/observability.</summary>
    string Name { get; }

    /// <summary>Provider type (e.g., "ollama", "openai", "lmstudio").</summary>
    string Type { get; }

    /// <summary>An optional model manager surfaced when the adapter can provision or retire models.</summary>
    IAiModelManager? ModelManager => null;

    /// <summary>Lists models available through this adapter.</summary>
    Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default);
}
