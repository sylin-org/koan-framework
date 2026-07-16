using Koan.Communication.Runtime;
using Koan.Core.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Communication.Signals;

/// <summary>
/// Internal contract for a framework-owned signal. The stable identifiers are infrastructure protocol, not an
/// application routing API.
/// </summary>
internal interface IFrameworkSignal<TSelf>
    where TSelf : struct, IFrameworkSignal<TSelf>
{
    static abstract string ContractId { get; }
    static abstract string GroupId { get; }
}

internal interface IHandleFrameworkSignal<TSignal>
    where TSignal : struct, IFrameworkSignal<TSignal>
{
    ValueTask Handle(TSignal signal, CancellationToken ct);
}

internal interface IFrameworkSignalPublisher
{
    string ProviderId { get; }
    string Assurance { get; }

    bool TryPublish<TSignal>(TSignal signal)
        where TSignal : struct, IFrameworkSignal<TSignal>;

    Task Start(CancellationToken ct);
    Task Stop(CancellationToken ct);
}

internal abstract class FrameworkSignalTargetBinding(
    Type signalType,
    Type handlerType,
    string contractId,
    string groupId)
    : CommunicationTargetBinding(signalType, handlerType, groupId)
{
    public Type SignalType => ContractType;
    public string ContractId { get; } = contractId;
}

internal sealed class FrameworkSignalTargetBinding<TSignal, THandler>()
    : FrameworkSignalTargetBinding(
        typeof(TSignal),
        typeof(THandler),
        TSignal.ContractId,
        TSignal.GroupId)
    where TSignal : struct, IFrameworkSignal<TSignal>
    where THandler : class, IHandleFrameworkSignal<TSignal>
{
    public override async Task<CommunicationTargetOutcome> Dispatch(
        IServiceProvider services,
        CommunicationEnvelope envelope,
        CancellationToken ct)
    {
        if (envelope is not FrameworkSignalEnvelope signalEnvelope
            || signalEnvelope.ContractType != typeof(TSignal))
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

internal static class FrameworkSignalServiceCollectionExtensions
{
    public static IServiceCollection AddFrameworkSignal<TSignal, THandler>(this IServiceCollection services)
        where TSignal : struct, IFrameworkSignal<TSignal>
        where THandler : class, IHandleFrameworkSignal<TSignal>
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<FrameworkSignalTargetBinding,
                FrameworkSignalTargetBinding<TSignal, THandler>>());
        return services;
    }
}
