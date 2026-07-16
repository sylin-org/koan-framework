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
        var transportRoutes = router?.Routes
            .Where(static route => route.Lane == CommunicationLane.Transport)
            .ToArray() ?? [];
        var eventRoutes = router?.Routes
            .Where(static route => route.Lane == CommunicationLane.Events)
            .ToArray() ?? [];
        var frameworkSignals = router?.For(CommunicationLane.FrameworkSignals);
        var frameworkBroadcasts = router?.For(CommunicationLane.FrameworkBroadcasts);

        foreach (var transport in transportRoutes)
        {
            var subject = Constants.Diagnostics.Subjects.TransportFor(transport.Channel);
            builder.AddElection(
                subject,
                transport.AdapterId,
                transport.Reason,
                transport.Priority,
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
            if (transport.Adapter.Descriptor.IsBuiltIn)
            {
                transportCapabilities.Add(Constants.Diagnostics.Capabilities.LocalSettlement);
                transportCapabilities.Add(Constants.Diagnostics.Capabilities.BoundedIngress);
            }
            else if (transport.Adapter.Descriptor.Assurance >= CommunicationDeliveryAssurance.Acknowledged)
            {
                transportCapabilities.Add(Constants.Diagnostics.Capabilities.ConfirmedPublication);
            }
            if (!transport.Adapter.Descriptor.SettlementObservable)
            {
                transportCapabilities.Add(Constants.Diagnostics.Capabilities.RemoteSettlementUnobservable);
            }
            builder.AddCapability(subject, transportCapabilities);
        }

        foreach (var events in eventRoutes)
        {
            var subject = Constants.Diagnostics.Subjects.EventsFor(events.Channel);
            builder.AddElection(
                subject,
                events.AdapterId,
                events.Reason,
                events.Priority,
                source: typeof(CommunicationCompositionContributor).FullName,
                factCode: Constants.Diagnostics.Codes.EventsSelected);
            var eventCapabilities = new List<string>
            {
                Constants.Diagnostics.Capabilities.EventsScalar,
                Constants.Diagnostics.Capabilities.EventsSet,
                Constants.Diagnostics.Capabilities.EventsStream,
                Constants.Diagnostics.Capabilities.OccurrenceIdentity,
                Constants.Diagnostics.Capabilities.TypedSubscriptions,
                Constants.Diagnostics.Capabilities.ZeroSubscriberAcceptance,
                Constants.Diagnostics.Capabilities.EventContextCarriage
            };
            if (events.Adapter.Descriptor.IsBuiltIn)
            {
                eventCapabilities.Add(Constants.Diagnostics.Capabilities.EventLocalSettlement);
                eventCapabilities.Add(Constants.Diagnostics.Capabilities.EventBoundedIngress);
            }
            builder.AddCapability(subject, eventCapabilities);
        }
        builder.AddConfigKey(Constants.Configuration.InProcessCapacity);
        builder.AddConfigKey(Constants.Configuration.MaxPayloadBytes);
        builder.AddConfigKey(Constants.Configuration.TransportProvider);
        builder.AddConfigKey(Constants.Configuration.EventsProvider);
        builder.AddConfigKey(Constants.Configuration.FrameworkSignalsProvider);
        builder.AddConfigKey(Constants.Configuration.FrameworkBroadcastsProvider);
        builder.AddConfigKey(Constants.Configuration.Channels);

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
            foreach (var transport in transportRoutes)
            {
                var subject = Constants.Diagnostics.Subjects.TransportBoundsFor(transport.Channel);
                if (transport.Adapter.Descriptor.IsBuiltIn)
                {
                    builder.AddObservation(
                        Constants.Diagnostics.Codes.TransportBounds,
                        subject,
                        $"Process-local Transport/{transport.Channel} uses " +
                        $"{Constants.Transport.ProcessMemoryAssurance} acceptance, a " +
                        $"{options.InProcessCapacity}-snapshot queue, and a " +
                        $"{options.MaxPayloadBytes}-byte publication limit.",
                        Constants.Diagnostics.Reasons.BoundedProcessMemory,
                        typeof(CommunicationCompositionContributor).FullName);
                }
                else
                {
                    builder.AddObservation(
                        Constants.Diagnostics.Codes.TransportBounds,
                        subject,
                        $"Transport/{transport.Channel} uses '{transport.AdapterId}' with " +
                        $"{transport.Assurance} publisher acceptance; handler settlement is " +
                        $"{(transport.Adapter.Descriptor.SettlementObservable ? "observable" : "not observable")} " +
                        "by the publisher.",
                        transport.Reason,
                        typeof(CommunicationCompositionContributor).FullName);
                }
            }

            foreach (var events in eventRoutes)
            {
                builder.AddObservation(
                    Constants.Diagnostics.Codes.EventsBounds,
                    Constants.Diagnostics.Subjects.EventsBoundsFor(events.Channel),
                    $"Events/{events.Channel} uses '{events.AdapterId}' with {events.Assurance} acceptance, a " +
                    $"{options.InProcessCapacity}-occurrence queue when process-local, and a " +
                    $"{options.MaxPayloadBytes}-byte publication limit; handler settlement is " +
                    $"{(events.Adapter.Descriptor.SettlementObservable ? "observable" : "not observable")}.",
                    events.Adapter.Descriptor.IsBuiltIn
                        ? Constants.Diagnostics.Reasons.BoundedProcessMemory
                        : events.Reason,
                    typeof(CommunicationCompositionContributor).FullName);
            }
        }

        var bindings = handlers?.TransportReceivers ?? [];
        builder.AddObservation(
            Constants.Diagnostics.Codes.ReceiversDiscovered,
            Constants.Diagnostics.Subjects.Receivers,
            $"Koan discovered {bindings.Count} typed Entity Transport receiver group(s) across " +
            $"{transportRoutes.Length} declared channel(s).",
            Constants.Diagnostics.Reasons.TypedDiscovery,
            typeof(CommunicationCompositionContributor).FullName);
        foreach (var binding in bindings)
        {
            foreach (var route in transportRoutes)
            {
                builder.AddObservation(
                    Constants.Diagnostics.Codes.ReceiverDiscovered,
                    Constants.Diagnostics.Subjects.ReceiverFor(route.Channel, binding.GroupIdentity),
                    $"{binding.GroupIdentity} receives isolated {binding.EntityType.FullName} snapshots on " +
                    $"Transport/{route.Channel} through '{route.AdapterId}'.",
                    Constants.Diagnostics.Reasons.TypedDiscovery,
                    typeof(CommunicationCompositionContributor).FullName);
            }
        }

        var subscriptions = handlers?.EventSubscriptions ?? [];
        builder.AddObservation(
            Constants.Diagnostics.Codes.SubscriptionsDiscovered,
            Constants.Diagnostics.Subjects.Subscriptions,
            $"Koan discovered {subscriptions.Count} typed Entity Event subscription group(s) across " +
            $"{eventRoutes.Length} declared channel(s). " +
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
            foreach (var route in eventRoutes)
            {
                builder.AddObservation(
                    Constants.Diagnostics.Codes.SubscriptionDiscovered,
                    Constants.Diagnostics.Subjects.SubscriptionFor(route.Channel, subscription.GroupIdentity),
                    $"{subscription.GroupIdentity} handles {subscription.EventType.FullName} for isolated " +
                    $"{subscription.EntityType.FullName} snapshots on Events/{route.Channel} through " +
                    $"'{route.AdapterId}' and {detailsPosture}.",
                    Constants.Diagnostics.Reasons.TypedDiscovery,
                    typeof(CommunicationCompositionContributor).FullName);
            }
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
