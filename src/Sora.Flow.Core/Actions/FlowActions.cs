using Sora.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sora.Flow.Actions;

public interface IFlowActions
{
    // Send a typed action for a model (seed/report/ping defaults)
    Task SeedAsync(string model, string referenceId, object payload, string? correlationId = null, CancellationToken ct = default);
    Task ReportAsync(string model, string referenceId, object payload, string? correlationId = null, CancellationToken ct = default);
    Task PingAsync(string model, string? correlationId = null, CancellationToken ct = default);
}

// Message contracts (aliases reflect intent; model-qualified via fields)
public sealed record FlowAction(
    string Model,
    string Verb,
    string? ReferenceId,
    object? Payload,
    string IdempotencyKey,
    string PartitionKey,
    string? CorrelationId = null,
    int DelaySeconds = 0);

public sealed record FlowAck(
    string Model,
    string Verb,
    string? ReferenceId,
    string Status, // ok|reject|busy|unsupported|error
    string? Message,
    string? CorrelationId = null);

public sealed record FlowReport(
    string Model,
    string? ReferenceId,
    object Stats,
    string? CorrelationId = null);

// Adapter metadata moved to Sora.Flow.Attributes.FlowAdapterAttribute

internal sealed class FlowActionsSender : IFlowActions
{
    private static string Key(string model, string verb, string? referenceId)
        => string.IsNullOrWhiteSpace(referenceId) ? $"{model}:{verb}" : $"{model}:{verb}:{referenceId}";

    public Task SeedAsync(string model, string referenceId, object payload, string? correlationId = null, CancellationToken ct = default)
        => SendAsync(model, "seed", referenceId, payload, correlationId, ct);

    public Task ReportAsync(string model, string referenceId, object payload, string? correlationId = null, CancellationToken ct = default)
        => SendAsync(model, "report", referenceId, payload, correlationId, ct);

    public Task PingAsync(string model, string? correlationId = null, CancellationToken ct = default)
        => SendAsync(model, "ping", null, null!, correlationId, ct);

    private static Task SendAsync(string model, string verb, string? referenceId, object? payload, string? correlationId, CancellationToken ct)
    {
    var idk = Key(model, verb, referenceId);
    var partition = model;
    var msg = new FlowAction(model, verb, referenceId, payload, idk, partition, correlationId);
    return msg.Send(cancellationToken: ct);
    }
}

public static class FlowActionsRegistration
{
    public static IServiceCollection AddFlowActions(this IServiceCollection services)
    {
        services.TryAddSingleton<IFlowActions, FlowActionsSender>();
        
        // Register FlowAction handler using modern .On<T>() pattern  
        Console.WriteLine("[FlowActions] DEBUG: Registering FlowAction handler...");
        services.TryAddSingleton<FlowActionHandler>();
        services.On<FlowAction>(async flowAction =>
        {
            Console.WriteLine($"[FlowActions] DEBUG: FlowAction handler called for {flowAction.Model}/{flowAction.Verb}");
            var serviceProvider = Sora.Core.Hosting.App.AppHost.Current;
            var handler = serviceProvider?.GetService<FlowActionHandler>();
            if (handler != null)
            {
                Console.WriteLine($"[FlowActions] DEBUG: Invoking FlowActionHandler for {flowAction.Model}");
                await handler.HandleAsync(null!, flowAction, CancellationToken.None);
                Console.WriteLine($"[FlowActions] DEBUG: FlowActionHandler completed for {flowAction.Model}");
            }
            else
            {
                Console.WriteLine("[FlowActions] ERROR: FlowActionHandler not found in service provider");
            }
        });
        
        return services;
    }
}
