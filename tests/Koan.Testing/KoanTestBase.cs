using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting;

namespace Koan.Testing;

/// <summary>
/// Base class for Koan Framework tests providing standardized DI setup.
/// Ensures logging is always configured. Tests should call framework setup methods like AddKoanDataCore().
/// </summary>
public abstract class KoanTestBase : IDisposable
{
    private ServiceProvider? _serviceProvider;
    private bool _disposed;

    /// <summary>
    /// Builds a service provider with standard Koan test configuration.
    /// Always includes logging. Tests should call AddKoanDataCore() or other framework setup methods.
    /// </summary>
    /// <param name="configure">Optional action to add test-specific services</param>
    /// <returns>Configured service provider</returns>
    protected IServiceProvider BuildServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // Core dependency ALWAYS needed for Koan framework tests
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Provide stub IHostApplicationLifetime for health tests
        services.AddSingleton<IHostApplicationLifetime>(new StubHostApplicationLifetime());

        // Allow test-specific service registrations (including AddKoanDataCore(), etc.)
        configure?.Invoke(services);

        // Build and cache the service provider for disposal
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }

    /// <summary>
    /// Disposes the service provider if created.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
                _serviceProvider = null;
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// Stub implementation of IHostApplicationLifetime for tests
    /// </summary>
    private class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
