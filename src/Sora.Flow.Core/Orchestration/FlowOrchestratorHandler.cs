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
internal sealed class FlowEntityOrchestratorHandler<T> where T : FlowEntity<T>, new()
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
            
            // Directly persist to Flow intake to avoid creating plain entity message backlog
            var sp = Sora.Core.Hosting.App.AppHost.Current;
            var sender = sp?.GetService(typeof(Sora.Flow.Sending.IFlowSender)) as Sora.Flow.Sending.IFlowSender;
            if (sender is null)
            {
                // Fallback to legacy path if sender not available
                await Sora.Messaging.MessagingExtensions.Send(entity, cancellationToken: ct);
            }
            else
            {
                var bag = FlowOrchestratorBagHelpers.ExtractBag(entity);
                var item = Sora.Flow.Sending.FlowSendPlainItem.Of<T>(bag, sourceId: "orchestrator", occurredAt: DateTimeOffset.UtcNow);
                await sender.SendAsync(new[] { item }, message: entity, hostType: typeof(T), ct: ct);
            }
            
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
internal sealed class FlowValueObjectOrchestratorHandler<T> where T : FlowValueObject<T>, new()
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
            
            var sp = Sora.Core.Hosting.App.AppHost.Current;
            var sender = sp?.GetService(typeof(Sora.Flow.Sending.IFlowSender)) as Sora.Flow.Sending.IFlowSender;
            if (sender is null)
            {
                await Sora.Messaging.MessagingExtensions.Send(valueObject, cancellationToken: ct);
            }
            else
            {
                var bag = FlowOrchestratorBagHelpers.ExtractBag(valueObject);
                var item = Sora.Flow.Sending.FlowSendPlainItem.Of<T>(bag, sourceId: "orchestrator", occurredAt: DateTimeOffset.UtcNow);
                await sender.SendAsync(new[] { item }, message: valueObject, hostType: typeof(T), ct: ct);
            }
            
            _logger.LogDebug("Successfully sent {ValueObjectType} value object to Flow intake", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {ValueObjectType} value object to Flow intake", typeof(T).Name);
            throw;
        }
    }
}

internal static partial class FlowOrchestratorBagHelpers
{
    internal static System.Collections.Generic.IDictionary<string, object?> ExtractBag(object entity)
    {
        var dict = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (entity is null) return dict;
        try
        {
            var props = entity.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            foreach (var p in props)
            {
                if (!p.CanRead) continue;
                var val = p.GetValue(entity);
                if (val is null || IsSimple(val.GetType()))
                {
                    dict[p.Name] = val;
                }
            }
        }
        catch { }
        return dict;
    }

    private static bool IsSimple(Type t)
    {
        if (t.IsPrimitive || t.IsEnum) return true;
        return t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid) || t == typeof(TimeSpan);
    }
}