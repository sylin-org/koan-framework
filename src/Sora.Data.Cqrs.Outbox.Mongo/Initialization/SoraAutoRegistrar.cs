using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Data.Cqrs.Outbox.Mongo.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Cqrs.Outbox.Mongo";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.BindOutboxOptions<MongoOutboxOptions>("Mongo");
        services.AddSingleton<MongoOutboxStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxStoreFactory, MongoOutboxFactory>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var s = cfg.GetSection("Sora:Cqrs:Profiles");
        var profiles = s.GetChildren().Select(c => c.Key).ToArray();
        report.AddSetting("Profiles", string.Join(",", profiles));
        var mongo = new MongoOutboxOptions();
        // try bind from standard place if exists
        // not strictly part of Sora:Cqrs but useful for diagnostics if configured via options
        report.AddNote("Outbox provider: Mongo");
    }
}
