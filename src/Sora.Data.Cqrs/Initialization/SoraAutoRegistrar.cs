using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Data.Abstractions;
using System.Linq;

namespace Sora.Data.Cqrs.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Cqrs";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Mirror existing initializer behavior
        services.AddOptions<CqrsOptions>().BindConfiguration("Sora:Cqrs").ValidateDataAnnotations();
        services.TryAddSingleton<ICqrsRouting, CqrsRouting>();
        services.BindOutboxOptions<InMemoryOutboxOptions>("InMemory");
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxStoreFactory, InMemoryOutboxFactory>());
        services.TryAddSingleton<IOutboxStore, OutboxStoreSelector>();
        services.TryDecorate(typeof(IDataRepository<,>), typeof(CqrsRepositoryDecorator<,>));
        services.AddHostedService<OutboxProcessor>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
    var defaultProfile = Sora.Core.Configuration.Read<string?>(cfg, Sora.Data.Cqrs.Infrastructure.Constants.Configuration.Keys.DefaultProfile, null);
    report.AddSetting("DefaultProfile", defaultProfile);
    var profiles = cfg.GetSection(Sora.Data.Cqrs.Infrastructure.Constants.Configuration.Profiles.Section).GetChildren().Select(c => c.Key).ToArray();
    report.AddSetting("Profiles", string.Join(",", profiles));
    }
}
