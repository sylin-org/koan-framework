using Koan.Canon.Model;

namespace Koan.Canon.Core.Orchestration;

/// <summary>
/// Interface for Canon orchestrators that process Canon entity transport messages.
/// </summary>
public interface ICanonOrchestrator
{
    /// <summary>
    /// Processes a Canon transport envelope containing entity data.
    /// </summary>
    Task ProcessCanonEntity(object transportEnvelope);
}


