using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Web.Auth.Contributors;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// Fires <see cref="IKoanAuthEventContributor.OnBootstrap"/> on every registered contributor
/// once during host startup. The dispatcher is resolved from a fresh service scope (it and its
/// contributor dependencies are scoped) so the singleton hosted service never holds a captive
/// scoped reference. Failures inside a single contributor are logged by the dispatcher and do
/// not prevent the host from coming up; a wholesale dispatch failure is logged here and also
/// non-fatal.
/// </summary>
internal sealed class AuthBootstrapHostedService : IHostedService
{
    private readonly IServiceProvider _rootServices;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AuthBootstrapHostedService> _logger;

    public AuthBootstrapHostedService(
        IServiceProvider rootServices,
        IHostEnvironment environment,
        ILogger<AuthBootstrapHostedService> logger)
    {
        _rootServices = rootServices;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _rootServices.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<AuthEventDispatcher>();
            _logger.LogDebug(
                "Koan.Web.Auth: dispatching OnBootstrap to {Count} contributors",
                dispatcher.Count);
            var ctx = new AuthBootstrapContext(scope.ServiceProvider, _environment);
            await dispatcher.DispatchBootstrap(ctx, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Koan.Web.Auth: auth bootstrap dispatch failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
