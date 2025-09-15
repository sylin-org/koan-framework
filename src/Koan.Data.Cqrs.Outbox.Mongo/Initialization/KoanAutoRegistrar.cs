using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;

namespace Koan.Data.Cqrs.Outbox.Mongo.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Cqrs.Outbox.Mongo";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.BindOutboxOptions<MongoOutboxOptions>("Mongo");
        services.AddSingleton<MongoOutboxStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxStoreFactory, MongoOutboxFactory>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var s = cfg.GetSection(Infrastructure.Constants.Configuration.Profiles.Section);
        var profiles = s.GetChildren().Select(c => c.Key).ToArray();
        report.AddSetting("Profiles", string.Join(",", profiles));
        var mongo = new MongoOutboxOptions();
        // try bind from standard place if exists
        // not strictly part of Koan:Cqrs but useful for diagnostics if configured via options
        report.AddNote("Outbox provider: Mongo");
    }
}
