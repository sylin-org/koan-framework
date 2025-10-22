using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Testing.Contracts;

namespace Koan.Testing.Fixtures;

public sealed class ServiceProviderFixture : IAsyncDisposable, IInitializableFixture
{
    private readonly Action<TestContext, IServiceCollection>? _configure;
    private ServiceProvider? _serviceProvider;

    public ServiceProviderFixture(Action<TestContext, IServiceCollection>? configure = null)
    {
        _configure = configure;
    }

    public IServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("Service provider has not been initialized.");

    public ValueTask InitializeAsync(TestContext context)
    {
        if (_serviceProvider is not null)
        {
            return ValueTask.CompletedTask;
        }

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();

        _configure?.Invoke(context, services);

        _serviceProvider = services.BuildServiceProvider();
        context.Diagnostics.Debug("service-provider.ready", new { services = _serviceProvider.GetHashCode() });
        return ValueTask.CompletedTask;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    public ValueTask DisposeAsync()
    {
        if (_serviceProvider is null)
        {
            return ValueTask.CompletedTask;
        }

        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            var task = asyncDisposable.DisposeAsync();
            _serviceProvider = null;
            return task;
        }

        _serviceProvider.Dispose();
        _serviceProvider = null;
        return ValueTask.CompletedTask;
    }

    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
