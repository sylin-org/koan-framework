using System.Globalization;
using Koan.Communication.Infrastructure;
using Koan.Communication.Runtime;
using Koan.Core;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Communication;

/// <summary>Reference = Intent boot module for Entity Communication.</summary>
public sealed class KoanCommunicationModule : KoanModule
{
    public override string Id => "Koan.Communication";

    public override void Register(IServiceCollection services)
        => services.AddKoanCommunication();

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var options = new CommunicationOptions();
        cfg.GetSection(Constants.Configuration.Section).Bind(options);
        var handlers = CommunicationHandlerCatalog.FromDiscovery();
        var receiverCount = handlers.TransportReceivers.Count;
        var subscriptionCount = handlers.EventSubscriptions.Count;

        module.Describe(Version);
        module.SetSetting(Constants.Provenance.Adapter, builder => builder
            .Label("Entity Transport provider")
            .Value("runtime election (see Koan facts)"));
        module.SetSetting(Constants.Provenance.Assurance, builder => builder
            .Label("Delivery assurance")
            .Value("provider-declared at runtime"));
        module.SetSetting(Constants.Provenance.ReceiverGroups, builder => builder
            .Label("Typed receiver groups")
            .Value(receiverCount.ToString(CultureInfo.InvariantCulture)));
        module.SetSetting(Constants.Provenance.EventAdapter, builder => builder
            .Label("Entity Events provider")
            .Value("runtime election (see Koan facts)"));
        module.SetSetting(Constants.Provenance.EventAssurance, builder => builder
            .Label("Event delivery assurance")
            .Value("provider-declared at runtime"));
        module.SetSetting(Constants.Provenance.EventSubscriptions, builder => builder
            .Label("Typed Event subscriptions")
            .Value(subscriptionCount.ToString(CultureInfo.InvariantCulture)));
        module.SetSetting(Constants.Provenance.FrameworkSignalsAdapter, builder => builder
            .Label("Framework-signal provider")
            .Value("runtime election (see Koan facts)"));
        module.SetSetting(Constants.Provenance.FrameworkBroadcastsAdapter, builder => builder
            .Label("Framework-broadcast provider")
            .Value("runtime election (see Koan facts)"));
        module.SetSetting(Constants.Provenance.Capacity, builder => builder
            .Label("Bounded local queue")
            .Value(options.InProcessCapacity.ToString(CultureInfo.InvariantCulture)));
        module.SetSetting(Constants.Provenance.MaxPayloadBytes, builder => builder
            .Label("Maximum payload bytes")
            .Value(options.MaxPayloadBytes.ToString(CultureInfo.InvariantCulture)));
    }
}
