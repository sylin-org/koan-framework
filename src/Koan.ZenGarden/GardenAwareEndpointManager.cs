namespace Koan.ZenGarden;

/// <summary>
/// Circuit breaker states for garden-aware endpoint management.
/// </summary>
public enum CircuitState
{
    /// <summary>Primary endpoint is healthy; all operations go to primary.</summary>
    Closed = 0,
    /// <summary>Primary is unavailable; operations should use fallback.</summary>
    Open = 1,
    /// <summary>Primary responded to SSE ready event; next operation attempts primary.</summary>
    HalfOpen = 2
}

/// <summary>
/// Shared component managing circuit breaker + SSE subscription + endpoint hot-swap
/// for any garden-aware adapter. Generic over TConnection so each adapter (S3/Mongo/Ollama)
/// provides its own connection type while sharing the orchestration logic.
///
/// <para>
/// Subscribe to ZenGarden SSE events via <see cref="ZenGardenClient.Subscribe"/>.
/// On SSE Ready event: creates new connection, transitions to HalfOpen.
/// On ReportFailure(): transitions to Open.
/// On successful operation after HalfOpen: transitions to Closed.
/// </para>
/// </summary>
public sealed class GardenAwareEndpointManager<TConnection> : IDisposable where TConnection : class
{
    private readonly Func<string, TConnection> _connectionFactory;
    private readonly Action<TConnection>? _disposeConnection;
    private readonly ILogger _logger;
    private readonly IDisposable _subscription;
    private readonly object _lock = new();

    private string? _currentEndpoint;
    private TConnection? _currentConnection;
    private CircuitState _state = CircuitState.Open;
    private bool _disposed;

    /// <summary>
    /// Raised when the endpoint changes due to SSE events. The new endpoint string is provided.
    /// </summary>
    public event Action<string>? OnEndpointChanged;

    /// <summary>
    /// Current endpoint URL, or null if not yet discovered.
    /// </summary>
    public string? CurrentEndpoint
    {
        get { lock (_lock) return _currentEndpoint; }
    }

    /// <summary>
    /// Whether the circuit is in a state that allows operations (Closed or HalfOpen).
    /// </summary>
    public bool IsAvailable
    {
        get { lock (_lock) return _state is CircuitState.Closed or CircuitState.HalfOpen; }
    }

    /// <summary>
    /// Current circuit state.
    /// </summary>
    public CircuitState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>
    /// Creates a new GardenAwareEndpointManager.
    /// </summary>
    /// <param name="subscription">ZenGarden subscription predicate for the target tool/offering.</param>
    /// <param name="connectionFactory">
    /// Factory delegate that creates a new TConnection from an endpoint URL.
    /// Called whenever the endpoint changes.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="disposeConnection">
    /// Optional delegate to dispose old connections when endpoint changes.
    /// If null and TConnection implements IDisposable, Dispose is called automatically.
    /// </param>
    public GardenAwareEndpointManager(
        ZenGardenSubscription subscription,
        Func<string, TConnection> connectionFactory,
        ILogger logger,
        Action<TConnection>? disposeConnection = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _disposeConnection = disposeConnection;
        _logger = logger;

        _subscription = ZenGarden.Client.Subscribe(
            subscription,
            OnAvailabilityEvent,
            new ZenGardenWatchOptions { EmitInitialState = true });
    }

    /// <summary>
    /// Creates a manager with an explicit initial endpoint (for non-SSE bootstrap).
    /// The circuit starts in Closed state with a pre-built connection.
    /// </summary>
    public GardenAwareEndpointManager(
        ZenGardenSubscription subscription,
        Func<string, TConnection> connectionFactory,
        ILogger logger,
        string initialEndpoint,
        Action<TConnection>? disposeConnection = null)
        : this(subscription, connectionFactory, logger, disposeConnection)
    {
        if (!string.IsNullOrWhiteSpace(initialEndpoint))
        {
            lock (_lock)
            {
                _currentEndpoint = initialEndpoint;
                _currentConnection = _connectionFactory(initialEndpoint);
                _state = CircuitState.Closed;
            }
        }
    }

    /// <summary>
    /// Gets the current connection. Returns null if circuit is Open or no connection exists.
    /// </summary>
    public TConnection? GetConnection()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Open)
                return null;

            return _currentConnection;
        }
    }

    /// <summary>
    /// Report a transport failure (e.g., 503 from S3, connection timeout).
    /// Opens the circuit immediately without waiting for SSE propagation.
    /// </summary>
    public void ReportFailure()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Open)
                return;

            var previous = _state;
            _state = CircuitState.Open;
            _logger.LogWarning(
                "GardenAwareEndpointManager: circuit opened (was {Previous}) for endpoint {Endpoint}",
                previous, _currentEndpoint);
        }
    }

    /// <summary>
    /// Report a successful operation. If circuit was HalfOpen, transitions to Closed.
    /// </summary>
    public void ReportSuccess()
    {
        lock (_lock)
        {
            if (_state != CircuitState.HalfOpen)
                return;

            _state = CircuitState.Closed;
            _logger.LogInformation(
                "GardenAwareEndpointManager: circuit closed (probe succeeded) for endpoint {Endpoint}",
                _currentEndpoint);
        }
    }

    private ValueTask OnAvailabilityEvent(ZenGardenAvailabilityEvent evt, CancellationToken ct)
    {
        switch (evt.Kind)
        {
            case ZenGardenAvailabilityEventKind.Online:
            case ZenGardenAvailabilityEventKind.Changed:
                HandleReady(evt);
                break;

            case ZenGardenAvailabilityEventKind.Offline:
                HandleOffline(evt);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private void HandleReady(ZenGardenAvailabilityEvent evt)
    {
        var snapshot = evt.Current;
        var newEndpoint = ResolveEndpointFromSnapshot(snapshot);

        if (string.IsNullOrWhiteSpace(newEndpoint))
        {
            _logger.LogDebug(
                "GardenAwareEndpointManager: SSE ready event but no endpoint in snapshot {Fqid}",
                snapshot.ToolFqid);
            return;
        }

        lock (_lock)
        {
            var endpointChanged = !string.Equals(_currentEndpoint, newEndpoint, StringComparison.OrdinalIgnoreCase);

            if (endpointChanged)
            {
                _logger.LogInformation(
                    "GardenAwareEndpointManager: endpoint changed {Old} → {New} (tool: {Fqid})",
                    _currentEndpoint, newEndpoint, snapshot.ToolFqid);

                // Dispose old connection
                if (_currentConnection is not null)
                {
                    DisposeConnectionSafe(_currentConnection);
                }

                _currentEndpoint = newEndpoint;
                _currentConnection = _connectionFactory(newEndpoint);
            }

            // Transition: Open → HalfOpen (probe needed), or stay Closed if already healthy
            if (_state == CircuitState.Open)
            {
                _state = CircuitState.HalfOpen;
            }
        }

        if (true) // always notify on ready
        {
            try
            {
                OnEndpointChanged?.Invoke(newEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GardenAwareEndpointManager: OnEndpointChanged handler threw");
            }
        }
    }

    private void HandleOffline(ZenGardenAvailabilityEvent evt)
    {
        lock (_lock)
        {
            if (_state == CircuitState.Open)
                return;

            _state = CircuitState.Open;
            _logger.LogWarning(
                "GardenAwareEndpointManager: circuit opened via SSE offline event for {Fqid}",
                evt.Current.ToolFqid);
        }
    }

    private static string? ResolveEndpointFromSnapshot(Models.ZenGardenToolSnapshot snapshot)
    {
        // Prefer connection URIs, then construct from hostname/IP + port
        if (snapshot.Connection?.Uris is { Count: > 0 } uris)
        {
            return uris[0];
        }

        var host = snapshot.Connection?.Hostname
            ?? snapshot.Connection?.Ip
            ?? snapshot.StoneEndpoint;

        if (string.IsNullOrWhiteSpace(host))
            return null;

        var port = snapshot.Connection?.Port;

        // If host looks like a full URL already, return it
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return host;
        }

        var protocol = snapshot.Connection?.Protocol ?? "http";
        return port.HasValue
            ? $"{protocol}://{host}:{port.Value}"
            : $"{protocol}://{host}";
    }

    private void DisposeConnectionSafe(TConnection connection)
    {
        try
        {
            if (_disposeConnection is not null)
            {
                _disposeConnection(connection);
            }
            else if (connection is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GardenAwareEndpointManager: error disposing old connection");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _subscription.Dispose();

        lock (_lock)
        {
            if (_currentConnection is not null)
            {
                DisposeConnectionSafe(_currentConnection);
                _currentConnection = null;
            }
        }
    }
}
