using Koan.Communication.Infrastructure;
using Koan.Communication.Runtime;
using Koan.Communication.Adapters;
using Koan.Communication.Signals;
using Koan.Core.Composition;
using Koan.Core.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Composition;

internal sealed class CommunicationCompositionContributor : IKoanCompositionContributor
{
    public void Contribute(KoanCompositionBuilder builder, IServiceProvider services)
    {
        var handlers = services.GetService<CommunicationHandlerCatalog>();
        var carriers = services.GetService<KoanContextCarrierRegistry>();
        var options = services.GetService<IOptions<CommunicationOptions>>()?.Value;
        var router = services.GetService<CommunicationRouter>();
        var transport = router?.For(CommunicationLane.Transport);
        var events = router?.For(CommunicationLane.Events);
        var frameworkSignals = router?.For(CommunicationLane.FrameworkSignals);
        var frameworkBroadcasts = router?.For(CommunicationLane.FrameworkBroadcasts);

        builder.AddElection(
            Constants.Diagnostics.Subjects.Transport,
            transport?.AdapterId ?? Constants.Transport.InProcessAdapter,
            transport?.Reason ?? Constants.Diagnostics.Reasons.BuiltInFloor,
            transport?.Priority,
            source: typeof(CommunicationCompositionContributor).FullName,
            factCode: Constants.Diagnostics.Codes.TransportSelected);
        var transportCapabilities = new List<string>
        {
            Constants.Diagnostics.Capabilities.Scalar,
            Constants.Diagnostics.Capabilities.Set,
            Constants.Diagnostics.Capabilities.Stream,
            Constants.Diagnostics.Capabilities.SnapshotCopy,
            Constants.Diagnostics.Capabilities.TypedReceivers,
            Constants.Diagnostics.Capabilities.ContextCarriage
        };
        if (transport?.Adapter.Descriptor.IsBuiltIn != false)
        {
            transportCapabilities.Add(Constants.Diagnostics.Capabilities.LocalSettlement);
            transportCapabilities.Add(Constants.Diagnostics.Capabilities.BoundedIngress);
        }
        else
        {
            transportCapabilities.Add(Constants.Diagnostics.Capabilities.ConfirmedPublication);
            transportCapabilities.Add(Constants.Diagnostics.Capabilities.RemoteSettlementUnobservable);
        }
        builder.AddCapability(
            Constants.Diagnostics.Subjects.Transport,
            transportCapabilities);
        builder.AddElection(
            Constants.Diagnostics.Subjects.Events,
            events?.AdapterId ?? Constants.Events.InProcessAdapter,
            events?.Reason ?? Constants.Diagnostics.Reasons.BuiltInFloor,
            events?.Priority,
            source: typeof(CommunicationCompositionContributor).FullName,
            factCode: Constants.Diagnostics.Codes.EventsSelected);
        builder.AddCapability(
            Constants.Diagnostics.Subjects.Events,
            [
                Constants.Diagnostics.Capabilities.EventsScalar,
                Constants.Diagnostics.Capabilities.EventsSet,
                Constants.Diagnostics.Capabilities.EventsStream,
                Constants.Diagnostics.Capabilities.OccurrenceIdentity,
                Constants.Diagnostics.Capabilities.TypedSubscriptions,
                Constants.Diagnostics.Capabilities.ZeroSubscriberAcceptance,
                Constants.Diagnostics.Capabilities.EventContextCarriage,
                Constants.Diagnostics.Capabilities.EventLocalSettlement,
                Constants.Diagnostics.Capabilities.EventBoundedIngress
            ]);
        builder.AddConfigKey(Constants.Configuration.InProcessCapacity);
        builder.AddConfigKey(Constants.Configuration.MaxPayloadBytes);
        builder.AddConfigKey(Constants.Configuration.TransportProvider);
        builder.AddConfigKey(Constants.Configuration.EventsProvider);
        builder.AddConfigKey(Constants.Configuration.FrameworkSignalsProvider);
        builder.AddConfigKey(Constants.Configuration.FrameworkBroadcastsProvider);

        builder.AddElection(
            Constants.Diagnostics.Subjects.FrameworkSignals,
            frameworkSignals?.AdapterId ?? Constants.Transport.InProcessAdapter,
            frameworkSignals?.Reason ?? Constants.Diagnostics.Reasons.BuiltInFloor,
            frameworkSignals?.Priority,
            source: typeof(CommunicationCompositionContributor).FullName,
            factCode: Constants.Diagnostics.Codes.FrameworkSignalsSelected);
        builder.AddCapability(
            Constants.Diagnostics.Subjects.FrameworkSignals,
            [
                Constants.Diagnostics.Capabilities.InternalFrameworkSignals,
                Constants.Diagnostics.Capabilities.BestEffortFallback,
                Constants.Diagnostics.Capabilities.BoundedSignalEgress
            ]);

        var frameworkBindings = services.GetServices<FrameworkMessageTargetBinding>().ToArray();
        var signalBindings = frameworkBindings
            .Where(static binding => binding.Lane == CommunicationLane.FrameworkSignals)
            .ToArray();
        builder.AddObservation(
            Constants.Diagnostics.Codes.FrameworkSignalGroupsDiscovered,
            Constants.Diagnostics.Subjects.FrameworkSignalGroups,
            $"Koan registered {signalBindings.Length} internal framework-signal group(s); " +
            "signals are bounded latency hints and are not an application Messaging API.",
            Constants.Diagnostics.Reasons.TypedDiscovery,
            typeof(CommunicationCompositionContributor).FullName);

        builder.AddElection(
            Constants.Diagnostics.Subjects.FrameworkBroadcasts,
            frameworkBroadcasts?.AdapterId ?? Constants.Transport.InProcessAdapter,
            frameworkBroadcasts?.Reason ?? Constants.Diagnostics.Reasons.BuiltInFloor,
            frameworkBroadcasts?.Priority,
            source: typeof(CommunicationCompositionContributor).FullName,
            factCode: Constants.Diagnostics.Codes.FrameworkBroadcastsSelected);
        builder.AddCapability(
            Constants.Diagnostics.Subjects.FrameworkBroadcasts,
            [
                Constants.Diagnostics.Capabilities.InternalFrameworkBroadcasts,
                Constants.Diagnostics.Capabilities.EveryActiveNode,
                Constants.Diagnostics.Capabilities.BestEffortFallback,
                Constants.Diagnostics.Capabilities.BoundedSignalEgress
            ]);
        var broadcastBindings = frameworkBindings
            .Where(static binding => binding.Lane == CommunicationLane.FrameworkBroadcasts)
            .ToArray();
        builder.AddObservation(
            Constants.Diagnostics.Codes.FrameworkBroadcastNodesDiscovered,
            Constants.Diagnostics.Subjects.FrameworkBroadcastNodes,
            $"Koan registered {broadcastBindings.Length} node-scoped framework-broadcast binding(s); " +
            "each active node within provider reach receives its own copy.",
            Constants.Diagnostics.Reasons.TypedDiscovery,
            typeof(CommunicationCompositionContributor).FullName);
        if (options is not null)
        {
            if (transport?.Adapter.Descriptor.IsBuiltIn != false)
            {
                builder.AddObservation(
                    Constants.Diagnostics.Codes.TransportBounds,
                    Constants.Diagnostics.Subjects.TransportBounds,
                    $"Process-local Transport uses {Constants.Transport.ProcessMemoryAssurance} acceptance, " +
                    $"a {options.InProcessCapacity}-snapshot queue, and a " +
                    $"{options.MaxPayloadBytes}-byte publication limit.",
                    Constants.Diagnostics.Reasons.BoundedProcessMemory,
                    typeof(CommunicationCompositionContributor).FullName);
            }
            else
            {
                builder.AddObservation(
                    Constants.Diagnostics.Codes.TransportBounds,
                    Constants.Diagnostics.Subjects.TransportBounds,
                    $"Transport/default uses '{transport!.AdapterId}' with {transport.Assurance} publisher acceptance; " +
                    "remote handler settlement is not observable by the publisher.",
                    transport.Reason,
                    typeof(CommunicationCompositionContributor).FullName);
            }
            builder.AddObservation(
                Constants.Diagnostics.Codes.EventsBounds,
                Constants.Diagnostics.Subjects.EventsBounds,
                $"Process-local Events use {Constants.Events.ProcessMemoryAssurance} acceptance, " +
                $"a {options.InProcessCapacity}-occurrence queue, and a " +
                $"{options.MaxPayloadBytes}-byte publication limit.",
                Constants.Diagnostics.Reasons.BoundedProcessMemory,
                typeof(CommunicationCompositionContributor).FullName);
        }

        var bindings = handlers?.TransportReceivers ?? [];
        builder.AddObservation(
            Constants.Diagnostics.Codes.ReceiversDiscovered,
            Constants.Diagnostics.Subjects.Receivers,
            $"Koan discovered {bindings.Count} typed Entity Transport receiver group(s).",
            Constants.Diagnostics.Reasons.TypedDiscovery,
            typeof(CommunicationCompositionContributor).FullName);
        foreach (var binding in bindings)
        {
            builder.AddObservation(
                Constants.Diagnostics.Codes.ReceiverDiscovered,
                Constants.Diagnostics.Subjects.ReceiverPrefix + binding.GroupIdentity,
                $"{binding.GroupIdentity} receives isolated {binding.EntityType.FullName} snapshots.",
                Constants.Diagnostics.Reasons.TypedDiscovery,
                typeof(CommunicationCompositionContributor).FullName);
        }

        var subscriptions = handlers?.EventSubscriptions ?? [];
        builder.AddObservation(
            Constants.Diagnostics.Codes.SubscriptionsDiscovered,
            Constants.Diagnostics.Subjects.Subscriptions,
            $"Koan discovered {subscriptions.Count} typed Entity Event subscription group(s). " +
            "Zero subscriptions remain a valid zero-target occurrence.",
            Constants.Diagnostics.Reasons.TypedDiscovery,
            typeof(CommunicationCompositionContributor).FullName);
        foreach (var subscription in subscriptions)
        {
            var detailsPosture = Attribute.IsDefined(
                subscription.EventType,
                typeof(EventDetailsRequiredAttribute),
                inherit: false)
                ? "requires explicit details"
                : "allows payloadless occurrences";
            builder.AddObservation(
                Constants.Diagnostics.Codes.SubscriptionDiscovered,
                Constants.Diagnostics.Subjects.SubscriptionPrefix + subscription.GroupIdentity,
                $"{subscription.GroupIdentity} handles {subscription.EventType.FullName} for " +
                $"isolated {subscription.EntityType.FullName} snapshots and {detailsPosture}.",
                Constants.Diagnostics.Reasons.TypedDiscovery,
                typeof(CommunicationCompositionContributor).FullName);
        }

        builder.AddObservation(
            Constants.Diagnostics.Codes.ContextCarriage,
            Constants.Diagnostics.Subjects.Context,
            $"Entity Events and Transport capture and restore " +
            $"{carriers?.Descriptors.Count ?? 0} host context carrier(s).",
            Constants.Diagnostics.Reasons.HostContextCarriers,
            typeof(CommunicationCompositionContributor).FullName);
    }
}
