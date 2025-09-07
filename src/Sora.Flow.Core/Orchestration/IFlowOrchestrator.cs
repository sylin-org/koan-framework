using Sora.Flow.Model;

namespace Sora.Flow.Core.Orchestration;

/// <summary>
/// Interface for Flow orchestrators that process Flow entity transport messages.
/// </summary>
public interface IFlowOrchestrator
{
    /// <summary>
    /// Processes a Flow transport envelope containing entity data.
    /// </summary>
    Task ProcessFlowEntity(object transportEnvelope);
}