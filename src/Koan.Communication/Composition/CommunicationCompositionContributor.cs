using Koan.Communication.Infrastructure;
using Koan.Communication.Runtime;
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

        builder.AddElection(
            Constants.Diagnostics.Subjects.Transport,
            Constants.Transport.InProcessAdapter,
            Constants.Diagnostics.Reasons.BuiltInFloor,
            source: typeof(CommunicationCompositionContributor).FullName,
            factCode: Constants.Diagnostics.Codes.TransportSelected);
        builder.AddCapability(
            Constants.Diagnostics.Subjects.Transport,
            [
                Constants.Diagnostics.Capabilities.Scalar,
                Constants.Diagnostics.Capabilities.Set,
                Constants.Diagnostics.Capabilities.Stream,
                Constants.Diagnostics.Capabilities.SnapshotCopy,
                Constants.Diagnostics.Capabilities.TypedReceivers,
                Constants.Diagnostics.Capabilities.ContextCarriage,
                Constants.Diagnostics.Capabilities.LocalSettlement,
                Constants.Diagnostics.Capabilities.BoundedIngress
            ]);
        builder.AddElection(
            Constants.Diagnostics.Subjects.Events,
            Constants.Events.InProcessAdapter,
            Constants.Diagnostics.Reasons.BuiltInFloor,
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
        if (options is not null)
        {
            builder.AddObservation(
                Constants.Diagnostics.Codes.TransportBounds,
                Constants.Diagnostics.Subjects.TransportBounds,
                $"Process-local Transport uses {Constants.Transport.ProcessMemoryAssurance} acceptance, " +
                $"a {options.InProcessCapacity}-snapshot queue, and a " +
                $"{options.MaxPayloadBytes}-byte publication limit.",
                Constants.Diagnostics.Reasons.BoundedProcessMemory,
                typeof(CommunicationCompositionContributor).FullName);
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
