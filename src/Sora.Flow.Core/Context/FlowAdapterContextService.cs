using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Flow.Attributes;

namespace Sora.Flow.Context;

/// <summary>
/// Hosted service wrapper that automatically sets FlowContext for adapter services
/// based on their [FlowAdapter] attribute.
/// </summary>
public class FlowAdapterContextService<T> : IHostedService where T : BackgroundService
{
    private readonly T _innerService;
    private readonly ILogger<FlowAdapterContextService<T>> _logger;
    private readonly FlowContext? _flowContext;
    private IDisposable? _contextScope;
    
    public FlowAdapterContextService(T innerService, ILogger<FlowAdapterContextService<T>> logger)
    {
        _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        _logger = logger;
        
        // Extract FlowContext from the FlowAdapter attribute
        var adapterAttr = typeof(T).GetCustomAttribute<FlowAdapterAttribute>(inherit: true);
        if (adapterAttr != null)
        {
            _flowContext = new FlowContext(adapterAttr.System, adapterAttr.Adapter, adapterAttr.DefaultSource);
            _logger.LogDebug("[FlowContext] Created context for adapter: System={System}, Adapter={Adapter}", 
                adapterAttr.System, adapterAttr.Adapter);
        }
        else
        {
            _logger.LogWarning("[FlowContext] No FlowAdapter attribute found on {ServiceType}", typeof(T).Name);
        }
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Set the flow context before starting the adapter service
        if (_flowContext != null)
        {
            _contextScope = FlowContext.Push(_flowContext);
            _logger.LogInformation("[FlowContext] Set context for adapter: {System}:{Adapter}", 
                _flowContext.System, _flowContext.Adapter);
        }
        
        // Start the actual adapter service
        await _innerService.StartAsync(cancellationToken);
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Stop the adapter service first
            await _innerService.StopAsync(cancellationToken);
        }
        finally
        {
            // Clean up the context
            _contextScope?.Dispose();
            _contextScope = null;
            
            if (_flowContext != null)
            {
                _logger.LogInformation("[FlowContext] Cleared context for adapter: {System}:{Adapter}", 
                    _flowContext.System, _flowContext.Adapter);
            }
        }
    }
}

/// <summary>
/// Extension methods for registering adapter services with automatic FlowContext management.
/// </summary>
public static class FlowAdapterServiceExtensions
{
    /// <summary>
    /// Registers an adapter service with automatic FlowContext management.
    /// The service will have its FlowContext set based on the [FlowAdapter] attribute.
    /// </summary>
    public static IServiceCollection AddFlowAdapter<T>(this IServiceCollection services) 
        where T : BackgroundService
    {
        // Register the actual adapter service
        services.AddSingleton<T>();
        
        // Register the context wrapper as the hosted service
        services.AddSingleton<IHostedService>(sp =>
        {
            var adapterService = sp.GetRequiredService<T>();
            var logger = sp.GetRequiredService<ILogger<FlowAdapterContextService<T>>>();
            return new FlowAdapterContextService<T>(adapterService, logger);
        });
        
        return services;
    }
}