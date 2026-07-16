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
        var receivers = services.GetService<TransportReceiverRegistry>();
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
        builder.AddConfigKey(Constants.Configuration.InProcessCapacity);
        builder.AddConfigKey(Constants.Configuration.MaxPayloadBytes);
        if (options is not null)
        {
            builder.AddObservation(
                Constants.Diagnostics.Codes.TransportBounds,
                Constants.Diagnostics.Subjects.Bounds,
                $"Process-local Transport uses {Constants.Transport.ProcessMemoryAssurance} acceptance, " +
                $"a {options.InProcessCapacity}-snapshot queue, and a {options.MaxPayloadBytes}-byte payload limit.",
                Constants.Diagnostics.Reasons.BoundedProcessMemory,
                typeof(CommunicationCompositionContributor).FullName);
        }

        var bindings = receivers?.All ?? [];
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

        builder.AddObservation(
            Constants.Diagnostics.Codes.ContextCarriage,
            Constants.Diagnostics.Subjects.Context,
            $"Entity Transport captures and restores {carriers?.Descriptors.Count ?? 0} host context carrier(s).",
            Constants.Diagnostics.Reasons.HostContextCarriers,
            typeof(CommunicationCompositionContributor).FullName);
    }
}
