using Koan.Communication.Adapters;
using Koan.Communication.Runtime;
using Koan.Core.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Communication.Signals;

/// <summary>
/// Internal contract for a framework-owned competing-group signal. Stable identifiers are infrastructure
/// protocol, not an application routing API.
/// </summary>
internal interface IFrameworkSignal<TSelf>
    where TSelf : struct, IFrameworkSignal<TSelf>
{
    static abstract string ContractId { get; }
    static abstract string GroupId { get; }
}

/// <summary>
/// Internal contract for a framework-owned signal that must reach every active node within provider reach.
/// </summary>
internal interface IFrameworkBroadcast<TSelf>
    where TSelf : struct, IFrameworkBroadcast<TSelf>
{
    static abstract string ContractId { get; }
}

internal interface IHandleFrameworkSignal<TSignal>
    where TSignal : struct, IFrameworkSignal<TSignal>
{
    ValueTask Handle(TSignal signal, CancellationToken ct);
}

internal interface IHandleFrameworkBroadcast<TSignal>
    where TSignal : struct, IFrameworkBroadcast<TSignal>
{
    ValueTask Handle(TSignal signal, CancellationToken ct);
}

internal interface IFrameworkSignalPublisher
{
    string ProviderId { get; }
    string Assurance { get; }
    string BroadcastProviderId { get; }
    string BroadcastAssurance { get; }
    bool BroadcastIsBuiltIn { get; }

    bool TryPublish<TSignal>(TSignal signal)
        where TSignal : struct, IFrameworkSignal<TSignal>;

    bool TryBroadcast<TSignal>(TSignal signal)
        where TSignal : struct, IFrameworkBroadcast<TSignal>;

    Task Start(CancellationToken ct);
    Task Stop(CancellationToken ct);
}

internal abstract class FrameworkMessageTargetBinding(
    Type signalType,
    Type handlerType,
    string contractId,
    string groupId,
    CommunicationLane lane,
    CommunicationBindingScope scope)
    : CommunicationTargetBinding(signalType, handlerType, groupId)
{
    public Type SignalType => ContractType;
    public string ContractId { get; } = contractId;
    public CommunicationLane Lane { get; } = lane;
    public CommunicationBindingScope Scope { get; } = scope;
}

internal sealed class FrameworkSignalTargetBinding<TSignal, THandler>()
    : FrameworkMessageTargetBinding(
        typeof(TSignal),
        typeof(THandler),
        TSignal.ContractId,
        TSignal.GroupId,
        CommunicationLane.FrameworkSignals,
        CommunicationBindingScope.ConsumerGroup)
    where TSignal : struct, IFrameworkSignal<TSignal>
    where THandler : class, IHandleFrameworkSignal<TSignal>
{
    public override async Task<CommunicationTargetOutcome> Dispatch(
        IServiceProvider services,
        CommunicationEnvelope envelope,
        CancellationToken ct)
    {
        if (envelope is not FrameworkSignalEnvelope signalEnvelope
            || signalEnvelope.ContractType != typeof(TSignal)
            || signalEnvelope.Lane != CommunicationLane.FrameworkSignals)
        {
            throw new InvalidOperationException(
                $"Framework signal handler '{HandlerType.FullName}' received an incompatible envelope.");
        }

        var signal = signalEnvelope.Payload.FromJson<TSignal>();
        var handler = (IHandleFrameworkSignal<TSignal>)ResolveHandler(services);
        await handler.Handle(signal, ct).ConfigureAwait(false);
        return CommunicationTargetOutcome.Delivered;
    }
}

internal sealed class FrameworkBroadcastTargetBinding<TSignal, THandler>()
    : FrameworkMessageTargetBinding(
        typeof(TSignal),
        typeof(THandler),
        TSignal.ContractId,
        $"koan.framework.node.{Guid.NewGuid():N}",
        CommunicationLane.FrameworkBroadcasts,
        CommunicationBindingScope.Node)
    where TSignal : struct, IFrameworkBroadcast<TSignal>
    where THandler : class, IHandleFrameworkBroadcast<TSignal>
{
    public override async Task<CommunicationTargetOutcome> Dispatch(
        IServiceProvider services,
        CommunicationEnvelope envelope,
        CancellationToken ct)
    {
        if (envelope is not FrameworkSignalEnvelope signalEnvelope
            || signalEnvelope.ContractType != typeof(TSignal)
            || signalEnvelope.Lane != CommunicationLane.FrameworkBroadcasts)
        {
            throw new InvalidOperationException(
                $"Framework broadcast handler '{HandlerType.FullName}' received an incompatible envelope.");
        }

        var signal = signalEnvelope.Payload.FromJson<TSignal>();
        var handler = (IHandleFrameworkBroadcast<TSignal>)ResolveHandler(services);
        await handler.Handle(signal, ct).ConfigureAwait(false);
        return CommunicationTargetOutcome.Delivered;
    }
}

internal static class FrameworkSignalServiceCollectionExtensions
{
    public static IServiceCollection AddFrameworkSignal<TSignal, THandler>(this IServiceCollection services)
        where TSignal : struct, IFrameworkSignal<TSignal>
        where THandler : class, IHandleFrameworkSignal<TSignal>
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<FrameworkMessageTargetBinding,
                FrameworkSignalTargetBinding<TSignal, THandler>>());
        return services;
    }

    public static IServiceCollection AddFrameworkBroadcast<TSignal, THandler>(this IServiceCollection services)
        where TSignal : struct, IFrameworkBroadcast<TSignal>
        where THandler : class, IHandleFrameworkBroadcast<TSignal>
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<FrameworkMessageTargetBinding,
                FrameworkBroadcastTargetBinding<TSignal, THandler>>());
        return services;
    }
}
