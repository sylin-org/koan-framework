using Koan.Communication.Adapters;
using Koan.Communication.Connector.RabbitMq.Discovery;
using Koan.Communication.Connector.RabbitMq.Infrastructure;
using Koan.Communication.Connector.RabbitMq.Orchestration;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Orchestration;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Communication.Connector.RabbitMq;

/// <summary>Reference = intent registration for the RabbitMQ Communication candidate.</summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => Constants.ProjectReference;
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCommunication();
        services.AddOptions<RabbitMqCommunicationOptions>()
            .BindConfiguration(Constants.Configuration.Section)
            .Validate(static value => value.Prefetch > 0, "RabbitMQ Communication Prefetch must be greater than zero.")
            .Validate(static value => value.PublishTimeout > TimeSpan.Zero,
                "RabbitMQ Communication PublishTimeout must be greater than zero.")
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, RabbitMqDiscoveryAdapter>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, RabbitMqOrchestrationEvaluator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICommunicationAdapter, RabbitMqCommunicationAdapter>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHealthContributor, RabbitMqHealthContributor>());
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddSetting("Provider", Constants.ProviderId);
        module.AddSetting("Claims", "Transport/default, FrameworkSignals/default");
        module.AddSetting("Assurance", "durably-acknowledged publication");
        module.AddNote("Candidacy is inert unless direct application intent or an explicit provider binding elects it.");
    }
}
