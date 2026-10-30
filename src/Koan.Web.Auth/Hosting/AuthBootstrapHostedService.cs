using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// Fires <c>OnBootstrap</c> on every registered <see cref="IKoanAuthFlowHandler"/> once during
/// host startup. The dispatcher is resolved
/// from a fresh service scope (it and its handler dependencies are scoped) so the singleton
/// hosted service never holds a captive scoped reference. Bootstrap fails closed: a handler's failure is
/// logged here and propagates, so the host does not start.
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
        await using var scope = _rootServices.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>();
        _logger.LogDebug(
            "Koan.Web.Auth: dispatching OnBootstrap to {Count} handlers",
            dispatcher.Count);
        var ctx = new AuthBootstrapContext(scope.ServiceProvider, _environment);
        try
        {
            await dispatcher.DispatchBootstrap(ctx, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogCritical(ex, "Koan.Web.Auth: a flow handler's bootstrap failed; the host does not start");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
