using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Sources;
using Koan.AI.Providers;
using Koan.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Initialization;

/// <summary>Activates one immutable host provider plan and commits its routing catalog exactly once.</summary>
internal sealed class AiProviderPlanInitializer
{
    private readonly IServiceProvider _services;
    private readonly AiProviderPlan _plan;
    private readonly InMemoryAdapterRegistry _adapters;
    private readonly IAiSourceRegistry _sources;
    private readonly ILogger<AiProviderPlanInitializer> _logger;

    public AiProviderPlanInitializer(
        IServiceProvider services,
        AiProviderPlan plan,
        InMemoryAdapterRegistry adapters,
        IAiSourceRegistry sources,
        ILogger<AiProviderPlanInitializer> logger)
    {
        _services = services;
        _plan = plan;
        _adapters = adapters;
        _sources = sources;
        _logger = logger;
    }

    public async Task Initialize(CancellationToken cancellationToken)
    {
        var activations = new List<(AiProviderRegistration Registration, AiProviderActivation Activation)>();

        foreach (var registration in _plan.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var activator = (IAiProviderActivator)_services.GetRequiredService(registration.ActivatorType);

            try
            {
                KoanLog.BootDebug(_logger, "ai.providers", "start", ("provider", registration.Id));
                var activation = await activator.Activate(_services, cancellationToken).ConfigureAwait(false);
                if (activation is null)
                {
                    KoanLog.BootInfo(_logger, "ai.providers", "inactive", ("provider", registration.Id));
                    continue;
                }

                Validate(registration, activation);
                activations.Add((registration, activation));
                KoanLog.BootDebug(_logger, "ai.providers", "ready",
                    ("provider", registration.Id),
                    ("sources", activation.Sources.Count));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                KoanLog.BootWarning(_logger, "ai.providers", "cancelled", ("provider", registration.Id));
                throw;
            }
            catch (Exception ex)
            {
                KoanLog.BootWarning(_logger, "ai.providers", "failed",
                    ("provider", registration.Id),
                    ("reason", ex.Message));
                KoanLog.BootDebug(_logger, "ai.providers", "failed-detail",
                    ("provider", registration.Id),
                    ("exception", ex.ToString()));
                throw;
            }
        }

        _adapters.Compile(activations.Select(static item => item.Activation.Adapter));
        foreach (var (_, activation) in activations)
        {
            foreach (var source in activation.Sources) _sources.RegisterSource(source);
        }

        KoanLog.BootInfo(_logger, "ai.providers", "compiled",
            ("available", _plan.Providers.Length),
            ("active", activations.Count),
            ("sources", activations.Sum(static item => item.Activation.Sources.Count)));
    }

    private void Validate(AiProviderRegistration registration, AiProviderActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation.Adapter);
        if (!string.Equals(registration.Id, activation.Adapter.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AI provider '{registration.Id}' activated adapter identity '{activation.Adapter.Id}'. " +
                "The module declaration and adapter identity must agree.");
        }

        var ownedAdapter = _services.GetService(activation.Adapter.GetType()) as IAiAdapter;
        if (!ReferenceEquals(ownedAdapter, activation.Adapter))
        {
            throw new InvalidOperationException(
                $"AI provider '{registration.Id}' returned an adapter that is not owned by this host's DI container. " +
                $"Register {activation.Adapter.GetType().FullName} as a singleton and resolve it during activation.");
        }

        foreach (var source in activation.Sources)
        {
            if (!string.Equals(source.Provider, registration.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"AI source '{source.Name}' names provider '{source.Provider}' but was contributed by '{registration.Id}'.");
            }
        }
    }
}
