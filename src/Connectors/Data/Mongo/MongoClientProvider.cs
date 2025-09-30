using Koan.Core.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo;

internal sealed class MongoClientProvider : IAdapterReadiness, IAsyncAdapterInitializer, IAsyncDisposable
{
    private readonly IOptionsMonitor<MongoOptions> _options;
    private readonly ILogger<MongoClientProvider>? _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly ReadinessStateManager _stateManager = new();
    private MongoClient? _client;
    private IMongoDatabase? _database;
    private string? _databaseName;

    public MongoClientProvider(IOptionsMonitor<MongoOptions> options, ILogger<MongoClientProvider>? logger = null)
    {
        _options = options;
        _logger = logger;
        _stateManager.StateChanged += (_, args) =>
        {
            if (args.CurrentState == AdapterReadinessState.Degraded)
            {
                _logger?.LogWarning("Mongo adapter entered degraded readiness");
            }

            ReadinessStateChanged?.Invoke(this, args);
        };
    }

    public AdapterReadinessState ReadinessState => _stateManager.State;

    public bool IsReady => _stateManager.IsReady;

    public TimeSpan ReadinessTimeout
    {
        get
        {
            var timeout = _options.CurrentValue.Readiness.Timeout;
            return timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(30);
        }
    }

    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged;

    public ReadinessStateManager StateManager => _stateManager;

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(_stateManager.IsReady);

    public async Task WaitForReadinessAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_stateManager.IsReady)
        {
            return;
        }

        if (_stateManager.State == AdapterReadinessState.Failed)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State, "Mongo adapter failed to initialize.");
        }

        var effective = timeout ?? ReadinessTimeout;
        if (effective <= TimeSpan.Zero)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                "Mongo readiness timeout is zero; readiness gating cannot wait.");
        }

        try
        {
            await _stateManager.WaitAsync(effective, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                $"Timed out waiting for Mongo readiness after {effective}.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (_stateManager.State == AdapterReadinessState.Failed)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                "Mongo adapter failed while waiting for readiness.", ex);
        }

        if (!_stateManager.IsReady)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                "Mongo adapter is not ready after waiting for readiness.");
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _stateManager.TransitionTo(AdapterReadinessState.Initializing);
        try
        {
            var options = _options.CurrentValue;
            await EnsureDatabaseAsync(options, ct).ConfigureAwait(false);
            _stateManager.TransitionTo(AdapterReadinessState.Ready);
        }
        catch (Exception ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            _logger?.LogError(ex, "Failed to initialize Mongo adapter");
            throw;
        }
    }

    public async Task<IMongoDatabase> GetDatabaseAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        return await EnsureDatabaseAsync(options, ct).ConfigureAwait(false);
    }

    private async Task<IMongoDatabase> EnsureDatabaseAsync(MongoOptions options, CancellationToken ct)
    {
        if (_database is not null && string.Equals(_databaseName, options.Database, StringComparison.Ordinal))
        {
            return _database;
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_database is null || !string.Equals(_databaseName, options.Database, StringComparison.Ordinal))
            {
                _logger?.LogDebug("Connecting to Mongo database {Database}", options.Database);
                _client = new MongoClient(options.ConnectionString);
                _database = _client.GetDatabase(options.Database);
                _databaseName = options.Database;
                await _database.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: ct).ConfigureAwait(false);
                if (_stateManager.State == AdapterReadinessState.Initializing)
                {
                    _stateManager.TransitionTo(AdapterReadinessState.Ready);
                }
            }
        }
        finally
        {
            _sync.Release();
        }

        return _database!;
    }

    public ValueTask DisposeAsync()
    {
        // MongoClient doesn't implement IAsyncDisposable, but we can dispose if it implements IDisposable
        if (_client is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _client = null;
        _database = null;
        _databaseName = null;
        _sync.Dispose();

        return ValueTask.CompletedTask;
    }
}

