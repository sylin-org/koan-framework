using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sora.Messaging;

/// <summary>
/// Manages the three-phase messaging lifecycle:
/// Phase 1: Handler registration (happens during startup)
/// Phase 2: Provider initialization (happens when app starts)
/// Phase 3: Go live and flush buffer (happens when provider is ready)
/// </summary>
internal class MessagingLifecycleService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessagingLifecycleService> _logger;
    private readonly AdaptiveMessageProxy _proxy;
    private readonly IEnumerable<IMessagingProvider> _providers;
    
    public MessagingLifecycleService(
        IServiceProvider serviceProvider,
        ILogger<MessagingLifecycleService> logger,
        AdaptiveMessageProxy proxy,
        IEnumerable<IMessagingProvider> providers)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _proxy = proxy;
        _providers = providers;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Phase 1 is already complete (handlers registered during ConfigureServices)
            var handlerRegistry = GetHandlerRegistry();
            var handlerCount = handlerRegistry.GetAllHandlers().Count;
            
            _logger.LogInformation("📋 Phase 1 Complete: {HandlerCount} message handlers registered", handlerCount);
            
            // Phase 2: Initialize messaging provider
            var provider = await SelectAndInitializeProviderAsync(cancellationToken);
            
            if (provider == null)
            {
                _logger.LogWarning("⚠️  No messaging providers available - messages will remain buffered");
                return;
            }
            
            // Phase 3: Go live and create consumers
            await _proxy.GoLiveAsync(provider, cancellationToken);
            
            // Create consumers for all registered handlers
            await handlerRegistry.CreateConsumersAsync(provider, cancellationToken);
            
            _logger.LogInformation("✅ Messaging lifecycle complete - system is live!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Failed to start messaging lifecycle");
            // Don't rethrow - app should still start even if messaging fails
        }
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 Stopping messaging lifecycle");
        
        // TODO: Gracefully stop consumers and close connections
        // For now, we'll just log
        _logger.LogInformation("🛑 Messaging lifecycle stopped");
    }
    
    private async Task<IMessageBus?> SelectAndInitializeProviderAsync(CancellationToken cancellationToken)
    {
        var availableProviders = _providers.ToList();
        
        if (!availableProviders.Any())
        {
            _logger.LogWarning("No messaging providers registered");
            return null;
        }
        
        _logger.LogDebug("🔍 Selecting messaging provider from {ProviderCount} available", availableProviders.Count);
        
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
                        _logger.LogDebug("🔌 Trying provider: {ProviderName} (Priority: {Priority})", provider.Name, provider.Priority);
                    }
                    else
                    {
                        _logger.LogDebug("🔄 Retry {Attempt}/{MaxRetries} for provider: {ProviderName}", attempt, maxRetries, provider.Name);
                    }
                    
                    // Check if provider can connect
                    var canConnect = await provider.CanConnectAsync(cancellationToken);
                    
                    if (!canConnect)
                    {
                        if (attempt < maxRetries)
                        {
                            var delay = baseDelayMs * attempt; // Exponential backoff
                            _logger.LogDebug("❌ Provider {ProviderName} cannot connect, retrying in {Delay}ms", provider.Name, delay);
                            await Task.Delay(delay, cancellationToken);
                            continue;
                        }
                        else
                        {
                            _logger.LogDebug("❌ Provider {ProviderName} cannot connect after {MaxRetries} attempts", provider.Name, maxRetries);
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
                            _logger.LogDebug("❌ Provider {ProviderName} is not healthy, retrying in {Delay}ms", provider.Name, delay);
                            await Task.Delay(delay, cancellationToken);
                            continue;
                        }
                        else
                        {
                            _logger.LogDebug("❌ Provider {ProviderName} is not healthy after {MaxRetries} attempts", provider.Name, maxRetries);
                            break; // Move to next provider
                        }
                    }
                    
                    _logger.LogInformation("🚀 Phase 2 Complete: Selected provider '{ProviderName}' after {Attempt} attempt(s)", provider.Name, attempt);
                    return bus;
                }
                catch (Exception ex)
                {
                    if (attempt < maxRetries)
                    {
                        var delay = baseDelayMs * attempt;
                        _logger.LogDebug(ex, "❌ Provider '{ProviderName}' initialization failed on attempt {Attempt}, retrying in {Delay}ms", provider.Name, attempt, delay);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning(ex, "❌ Provider '{ProviderName}' initialization failed after {MaxRetries} attempts", provider.Name, maxRetries);
                        break; // Move to next provider
                    }
                }
            }
        }
        
        _logger.LogWarning("❌ No providers could be initialized after retry attempts");
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
        services.AddHostedService<MessagingLifecycleService>();
        return services;
    }
}