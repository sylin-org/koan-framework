using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Cqrs.Outbox.Connector.Mongo.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Cqrs.Outbox.Connector.Mongo";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.BindOutboxOptions<MongoOutboxOptions>("Mongo");
        services.AddSingleton<MongoOutboxStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxStoreFactory, MongoOutboxFactory>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var s = cfg.GetSection(Infrastructure.Constants.Configuration.Profiles.Section);
        var profiles = s.GetChildren().Select(c => c.Key).ToArray();
        module.AddSetting("Profiles", string.Join(",", profiles));
        var mongo = new MongoOutboxOptions();
        // try bind from standard place if exists
        // not strictly part of Koan:Cqrs but useful for diagnostics if configured via options
        module.AddNote("Outbox provider: Mongo");
    }
}


