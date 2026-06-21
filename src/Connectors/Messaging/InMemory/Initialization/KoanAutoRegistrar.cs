using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Messaging.Connector.InMemory.Initialization;

/// <summary>
/// Auto-registers the in-process Channels messaging provider when the package is referenced
/// (Reference = Intent). It fills the reserved Priority-10 slot, so referencing this connector is what
/// makes the messaging core's in-memory fallback real rather than advertised — the boot report now only
/// claims an in-memory provider when one is actually present.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Messaging.Connector.InMemory";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient<IMessagingProvider, InMemoryMessagingProvider>());
        services.AddKoanMessaging();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddSetting("Provider", "InMemory (System.Threading.Channels)");
        module.AddSetting("Priority", "10 (fallback floor)");
        module.AddSetting("Durability", "non-durable (in-process; durable work uses the Jobs ledger)");
        module.AddNote("In-process messaging available — zero broker, single-binary friendly.");
    }
}
