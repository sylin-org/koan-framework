using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Sora.Data.Cqrs;

/// <summary>
/// Auto-wires implicit CQRS when enabled via options.
/// </summary>
public sealed class CqrsSoraInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
    services.AddOptions<CqrsOptions>().BindConfiguration("Sora:Cqrs").ValidateDataAnnotations();
    services.TryAddSingleton<ICqrsRouting, CqrsRouting>();
    // Register factories and selector: referencing a provider package contributes its factory.
    services.BindOutboxOptions<InMemoryOutboxOptions>("InMemory");
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxStoreFactory, InMemoryOutboxFactory>());
    services.TryAddSingleton<IOutboxStore, OutboxStoreSelector>();
        // Decorate repositories only when CQRS is enabled. We can still decorate and short-circuit by checking options at runtime.
        services.TryDecorate(typeof(IDataRepository<,>), typeof(CqrsRepositoryDecorator<,>));
    // Background processor to drain the outbox and apply basic projections in implicit mode
    services.AddHostedService<OutboxProcessor>();
    }
}
