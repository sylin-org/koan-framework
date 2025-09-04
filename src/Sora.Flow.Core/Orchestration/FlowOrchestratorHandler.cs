using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sora.Flow.Model;
using Sora.Flow.Sending;

namespace Sora.Flow.Orchestration;

/// <summary>
/// Auto-generated handler for FlowEntity instances.
/// Implements the orchestrator pattern where adapters send entities → orchestrator → intake queue → business logic.
/// </summary>
/// <typeparam name="T">The FlowEntity type</typeparam>
public sealed class FlowEntityOrchestratorHandler<T> where T : FlowEntity<T>, new()
{
    private readonly ILogger<FlowEntityOrchestratorHandler<T>> _logger;

    public FlowEntityOrchestratorHandler(ILogger<FlowEntityOrchestratorHandler<T>> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles incoming FlowEntity instances by sending them to Flow intake for business logic processing.
    /// </summary>
    public async Task HandleAsync(T entity, CancellationToken ct = default)
    {
        if (entity is null)
        {
            _logger.LogWarning("Received null {EntityType} entity, skipping", typeof(T).Name);
            return;
        }

        try
        {
            _logger.LogDebug("Orchestrator handling {EntityType} entity: {EntityId}", typeof(T).Name, entity.Id);
            
            // Use internal method to send directly to Flow intake (bypass messaging loop)
            await entity.SendToFlowIntake(ct: ct);
            
            _logger.LogDebug("Successfully sent {EntityType} entity {EntityId} to Flow intake", typeof(T).Name, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {EntityType} entity {EntityId} to Flow intake", typeof(T).Name, entity?.Id);
            throw;
        }
    }
}

/// <summary>
/// Auto-generated handler for FlowValueObject instances.
/// Implements the orchestrator pattern where adapters send value objects → orchestrator → intake queue → business logic.
/// </summary>
/// <typeparam name="T">The FlowValueObject type</typeparam>
public sealed class FlowValueObjectOrchestratorHandler<T> where T : FlowValueObject<T>, new()
{
    private readonly IFlowSender _flowSender;
    private readonly ILogger<FlowValueObjectOrchestratorHandler<T>> _logger;

    public FlowValueObjectOrchestratorHandler(IFlowSender flowSender, ILogger<FlowValueObjectOrchestratorHandler<T>> logger)
    {
        _flowSender = flowSender ?? throw new ArgumentNullException(nameof(flowSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles incoming FlowValueObject instances by sending them to Flow intake for business logic processing.
    /// </summary>
    public async Task HandleAsync(T valueObject, CancellationToken ct = default)
    {
        if (valueObject is null)
        {
            _logger.LogWarning("Received null {ValueObjectType} value object, skipping", typeof(T).Name);
            return;
        }

        try
        {
            _logger.LogDebug("Orchestrator handling {ValueObjectType} value object", typeof(T).Name);
            
            // Use internal method to send directly to Flow intake (bypass messaging loop)
            await valueObject.SendToFlowIntake(ct: ct);
            
            _logger.LogDebug("Successfully sent {ValueObjectType} value object to Flow intake", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {ValueObjectType} value object to Flow intake", typeof(T).Name);
            throw;
        }
    }
}