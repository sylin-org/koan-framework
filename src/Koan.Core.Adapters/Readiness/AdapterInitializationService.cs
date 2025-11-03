using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Core.Adapters;

public interface IAdapterInitializationOrder
{
    int Priority { get; }

    IEnumerable<IAsyncAdapterInitializer> Apply(IEnumerable<IAsyncAdapterInitializer> initializers);
}

public interface IAdapterInitializationRetryPolicy
{
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct);
}

public interface IRetryPolicyProvider
{
    IAdapterInitializationRetryPolicy GetPolicy(string adapterType);
}

internal sealed class DefaultAdapterInitializationRetryPolicy : IAdapterInitializationRetryPolicy
{
    private readonly TimeSpan _timeout;
    private readonly ILogger? _logger;
    private readonly string _adapterType;

    public DefaultAdapterInitializationRetryPolicy(TimeSpan timeout, ILogger? logger, string adapterType)
    {
        _timeout = timeout;
        _logger = logger;
        _adapterType = adapterType;
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(_timeout);
        }

        try
        {
            await action(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger?.LogError("Adapter {AdapterType} initialization timed out after {Timeout}", _adapterType, _timeout);
            throw;
        }
    }
}

internal sealed class DefaultRetryPolicyProvider : IRetryPolicyProvider
{
    private readonly AdaptersReadinessOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    public DefaultRetryPolicyProvider(IOptions<AdaptersReadinessOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
    }

    public IAdapterInitializationRetryPolicy GetPolicy(string adapterType)
    {
        var logger = _loggerFactory.CreateLogger<DefaultAdapterInitializationRetryPolicy>();
        return new DefaultAdapterInitializationRetryPolicy(_options.InitializationTimeout, logger, adapterType);
    }
}

internal sealed class AdapterInitializationService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AdapterInitializationService> _logger;
    private readonly IEnumerable<IAdapterInitializationOrder> _orderingPolicies;
    private readonly IRetryPolicyProvider _retryPolicies;

    public AdapterInitializationService(
        IServiceProvider services,
        ILogger<AdapterInitializationService> logger,
        IEnumerable<IAdapterInitializationOrder> orderingPolicies,
        IRetryPolicyProvider retryPolicies)
    {
        _services = services;
        _logger = logger;
        _orderingPolicies = orderingPolicies;
        _retryPolicies = retryPolicies;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var initializers = scope.ServiceProvider.GetServices<IAsyncAdapterInitializer>().ToList();

        if (initializers.Count == 0)
        {
            _logger.LogDebug("No async adapter initializers registered");
            return;
        }

        _logger.LogInformation("Initializing {Count} async adapters", initializers.Count);

        foreach (var wave in ApplyOrdering(initializers))
        {
            var snapshot = wave.ToArray();
            if (snapshot.Length == 0)
            {
                continue;
            }

            _logger.LogDebug("Starting initialization wave with {Count} adapters", snapshot.Length);
            var tasks = snapshot.Select(initializer => InitializeAsync(initializer, cancellationToken));
            await Task.WhenAll(tasks);
        }

        _logger.LogInformation("Async adapter initialization completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private IEnumerable<IReadOnlyCollection<IAsyncAdapterInitializer>> ApplyOrdering(IReadOnlyCollection<IAsyncAdapterInitializer> initializers)
    {
        if (!_orderingPolicies.Any())
        {
            return new[] { (IReadOnlyCollection<IAsyncAdapterInitializer>)initializers.ToArray() };
        }

        var remaining = initializers;
        var waves = new List<IReadOnlyCollection<IAsyncAdapterInitializer>>();

        foreach (var policy in _orderingPolicies.OrderBy(p => p.Priority))
        {
            var slice = policy.Apply(remaining).ToArray();
            if (slice.Length > 0)
            {
                waves.Add(slice);
                remaining = remaining.Except(slice).ToArray();
            }
        }

        if (remaining.Any())
        {
            waves.Add(remaining.ToArray());
        }

        return waves;
    }

    private async Task InitializeAsync(IAsyncAdapterInitializer initializer, CancellationToken cancellationToken)
    {
        var adapterType = initializer.GetType().Name;
        var policy = _retryPolicies.GetPolicy(adapterType);

        try
        {
            _logger.LogDebug("Initializing adapter {Adapter}", adapterType);
            await policy.ExecuteAsync(ct => initializer.InitializeAsync(ct), cancellationToken);
            _logger.LogDebug("Adapter {Adapter} initialized", adapterType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adapter {Adapter} failed to initialize", adapterType);
        }
    }
}
