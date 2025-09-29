using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core.BackgroundServices;

namespace Koan.Messaging;

/// <summary>
/// Manages the three-phase messaging lifecycle:
/// Phase 1: Handler registration (happens during startup)
/// Phase 2: Provider initialization (happens when app starts)
/// Phase 3: Go live and flush buffer (happens when provider is ready)
/// </summary>
[KoanBackgroundService(RunInProduction = true)]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Messaging.Ready, EventArgsType = typeof(MessagingReadyEventArgs))]
[ServiceEvent(Koan.Core.Events.KoanServiceEvents.Messaging.Failed, EventArgsType = typeof(MessagingFailedEventArgs))]
internal class MessagingLifecycleService : KoanFluentServiceBase
{
    private readonly AdaptiveMessageProxy _proxy;
    private readonly IEnumerable<IMessagingProvider> _providers;
    private readonly IServiceProvider _serviceProvider;
    
    public MessagingLifecycleService(
        ILogger<MessagingLifecycleService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        AdaptiveMessageProxy proxy,
        IEnumerable<IMessagingProvider> providers)
        : base(logger, configuration)
    {
    _serviceProvider = serviceProvider;
        _proxy = proxy;
        _providers = providers;
    }
    
    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Phase 1 is already complete (handlers registered during ConfigureServices)
            var handlerRegistry = GetHandlerRegistry();
            var allHandlers = handlerRegistry.GetAllHandlers();
            var handlerCount = allHandlers.Count;
            Logger.LogInformation("[Messaging] Phase 1: {HandlerCount} handlers registered", handlerCount);
            try
            {
                foreach (var kvp in allHandlers)
                {
                    Logger.LogInformation("Handler registered: {HandlerType}", kvp.Key.FullName ?? kvp.Key.Name);
                }
            }
            catch { /* ignore diagnostics failures */ }
            
            // Phase 2: Initialize messaging provider
            var provider = await SelectAndInitializeProviderAsync(cancellationToken);
            
            if (provider == null)
            {
                Logger.LogWarning("[Messaging] No providers available - messages will remain buffered");
                await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Messaging.Failed, new MessagingFailedEventArgs
                {
                    Reason = "No providers available",
                    FailedAt = DateTimeOffset.UtcNow
                });
                return;
            }
            
            // Phase 3: Go live and create consumers
            await _proxy.GoLiveAsync(provider, cancellationToken);
            
            // Create consumers for all registered handlers
            await handlerRegistry.CreateConsumersAsync(provider, cancellationToken);
            
            Logger.LogInformation("[Messaging] System ready - {HandlerCount} consumers active", handlerCount);
            
            await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Messaging.Ready, new MessagingReadyEventArgs
            {
                HandlerCount = handlerCount,
                ProviderName = provider.GetType().Name,
                ReadyAt = DateTimeOffset.UtcNow
            });
            
            // Keep service running
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Messaging] Failed to start messaging lifecycle");
            
            await EmitEventAsync(Koan.Core.Events.KoanServiceEvents.Messaging.Failed, new MessagingFailedEventArgs
            {
                Reason = ex.Message,
                FailedAt = DateTimeOffset.UtcNow,
                Exception = ex
            });
            
            // Don't rethrow - app should still start even if messaging fails
        }
    }
    
    [ServiceAction(Koan.Core.Actions.KoanServiceActions.Messaging.RestartMessaging)]
    public virtual Task RestartMessagingAction(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Manual messaging restart requested");
        // Could implement restart logic here
        return Task.CompletedTask;
    }
    
    private async Task<IMessageBus?> SelectAndInitializeProviderAsync(CancellationToken cancellationToken)
    {
        var availableProviders = _providers.ToList();
        
        if (!availableProviders.Any())
        {
            Logger.LogWarning("No messaging providers registered");
            return null;
        }
        
        Logger.LogDebug("[Messaging] Selecting provider from {ProviderCount} available", availableProviders.Count);
        
        // Retry configuration
        const int maxRetries = 5;
        const int baseDelayMs = 2000; // Start with 2 seconds
        
        // Try providers in priority order with retry logic
        foreach (var provider in availableProviders.OrderByDescending(p => p.Priority))
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt == 1)
                    {
                        Logger.LogDebug("[Messaging] Trying provider: {ProviderName} (Priority: {Priority})", provider.Name, provider.Priority);
                    }
                    else
                    {
                        Logger.LogDebug("[Messaging] Retry {Attempt}/{MaxRetries} for provider: {ProviderName}", attempt, maxRetries, provider.Name);
                    }
                    
                    // Check if provider can connect
                    var canConnect = await provider.CanConnectAsync(cancellationToken);
                    
                    if (!canConnect)
                    {
                        if (attempt < maxRetries)
                        {
                            var delay = baseDelayMs * attempt; // Exponential backoff
                            Logger.LogDebug("[Messaging] Provider {ProviderName} cannot connect, retrying in {Delay}ms", provider.Name, delay);
                            await Task.Delay(delay, cancellationToken);
                            continue;
                        }
                        else
                        {
                            Logger.LogDebug("[Messaging] Provider {ProviderName} cannot connect after {MaxRetries} attempts", provider.Name, maxRetries);
                            break; // Move to next provider
                        }
                    }
                    
                    // Initialize the provider
                    var bus = await provider.CreateBusAsync(cancellationToken);
                    
                    // Verify it's actually working
                    var isHealthy = await bus.IsHealthyAsync(cancellationToken);
                    
                    if (!isHealthy)
                    {
                        if (attempt < maxRetries)
                        {
                            var delay = baseDelayMs * attempt;
                            Logger.LogDebug("[Messaging] Provider {ProviderName} not healthy, retrying in {Delay}ms", provider.Name, delay);
                            await Task.Delay(delay, cancellationToken);
                            continue;
                        }
                        else
                        {
                            Logger.LogDebug("[Messaging] Provider {ProviderName} not healthy after {MaxRetries} attempts", provider.Name, maxRetries);
                            break; // Move to next provider
                        }
                    }
                    
                    Logger.LogInformation("[Messaging] Phase 2: Selected provider '{ProviderName}' after {Attempt} attempt(s)", provider.Name, attempt);
                    return bus;
                }
                catch (Exception ex)
                {
                    if (attempt < maxRetries)
                    {
                        var delay = baseDelayMs * attempt;
                        Logger.LogDebug(ex, "[Messaging] Provider '{ProviderName}' failed on attempt {Attempt}, retrying in {Delay}ms", provider.Name, attempt, delay);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        Logger.LogWarning(ex, "[Messaging] Provider '{ProviderName}' failed after {MaxRetries} attempts", provider.Name, maxRetries);
                        break; // Move to next provider
                    }
                }
            }
        }
        
        Logger.LogWarning("[Messaging] No providers could be initialized after retry attempts");
        return null;
    }
    
    private HandlerRegistry GetHandlerRegistry()
    {
        var options = _serviceProvider.GetService<IOptions<HandlerRegistry>>();
        return options?.Value ?? new HandlerRegistry();
    }
}

/// <summary>
/// Extension methods for registering the messaging lifecycle.
/// </summary>
public static class MessagingLifecycleExtensions
{
    /// <summary>
    /// Adds the messaging lifecycle service to dependency injection.
    /// This orchestrates provider selection, buffer flushing, and consumer creation.
    /// </summary>
    internal static IServiceCollection AddMessagingLifecycle(this IServiceCollection services)
    {
        // Service is now auto-discovered via KoanBackgroundService attribute
        return services;
    }
}