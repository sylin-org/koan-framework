using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

/// <summary>
/// Zen Garden client implementing UDP discovery and Stone HTTP API.
/// </summary>
public sealed class ZenGardenClient : IZenGardenClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ZenGardenClient> _logger;
    private readonly ZenGardenOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    
    // Two-level cache:
    // 1. Bound Stone - which Stone to talk to (in-memory singleton)
    // 2. Offering cache - resolved URLs per offering (in-memory, app lifetime)
    private readonly ConcurrentDictionary<string, ResolvedService> _offeringCache = new(StringComparer.OrdinalIgnoreCase);
    
    // Hot-cache topology: discovered Stones for fast parallel lookup
    private readonly ConcurrentDictionary<string, Stone> _topologyCache = new(StringComparer.OrdinalIgnoreCase);
    
    // Cached Stone binding
    private Stone? _boundStone;
    private readonly object _bindLock = new();

    public ZenGardenClient(
        HttpClient httpClient,
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options ?? new ZenGardenOptions();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }
    
    /// <inheritdoc />
    public Stone? BoundStone => _boundStone;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Stone>> DiscoverStonesAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(_options.DiscoveryTimeoutSeconds);
        
        var request = new DiscoveryRequest
        {
            Data = new DiscoveryRequestData
            {
                RequestId = $"koan-{Guid.NewGuid():N}",
                Requester = "koan-framework"
            }
        };
        
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, _jsonOptions);
        var stones = new Dictionary<string, Stone>(StringComparer.OrdinalIgnoreCase);
        
        var multicastGroup = _options.MulticastGroup ?? Constants.Discovery.MulticastGroup;
        var port = _options.DiscoveryPort ?? Constants.Discovery.Port;
        
        // Find best LAN interface for reliable broadcast on multi-homed systems (WSL, Hyper-V)
        var lanIP = GetLanBindAddress();
        
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.ReceiveTimeout = 500; // Short timeout for responsive collection
        
        // Bind to the LAN interface on the discovery port to receive responses
        // This is critical on Windows with multiple interfaces
        var bindAddress = lanIP ?? IPAddress.Any;
        socket.Bind(new IPEndPoint(bindAddress, port));
        _logger.LogDebug("Bound to {Address}:{Port} for discovery", bindAddress, port);
        
        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(multicastGroup), port);
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, port);
        
        try
        {
            await socket.SendToAsync(payload, SocketFlags.None, multicastEndpoint, cancellationToken);
            _logger.LogDebug("Sent discovery request to multicast {Endpoint}", multicastEndpoint);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("Multicast send failed, trying broadcast: {Error}", ex.Message);
        }
        
        try
        {
            await socket.SendToAsync(payload, SocketFlags.None, broadcastEndpoint, cancellationToken);
            _logger.LogDebug("Sent discovery request to broadcast {Endpoint}", broadcastEndpoint);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Broadcast send failed: {Error}", ex.Message);
        }
        
        // Collect responses with per-receive timeout
        var deadline = DateTime.UtcNow.Add(timeout.Value);
        var buffer = new byte[4096];
        
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Calculate remaining time for this receive
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;
                
                // Use a timeout for each receive to avoid blocking forever
                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(500, remaining.TotalMilliseconds)));
                
                var result = await socket.ReceiveFromAsync(
                    buffer, 
                    SocketFlags.None, 
                    new IPEndPoint(IPAddress.Any, 0), 
                    receiveCts.Token);
                
                var json = Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);
                var response = JsonSerializer.Deserialize<DiscoveryResponse>(json, _jsonOptions);
                
                if (response?.Type == "discovery_response" && response.Data != null)
                {
                    var stone = response.Data.ToStone();
                    
                    // Fix loopback addresses - use the source IP instead
                    if (stone.StoneEndpoint.Contains("127.0.0.1") && result.RemoteEndPoint is IPEndPoint remoteEp)
                    {
                        var fixedEndpoint = stone.StoneEndpoint.Replace("127.0.0.1", remoteEp.Address.ToString());
                        stone = stone with { StoneEndpoint = fixedEndpoint };
                    }
                    
                    if (!stones.ContainsKey(stone.CacheKey))
                    {
                        stones[stone.CacheKey] = stone;
                        _topologyCache[stone.CacheKey] = stone;
                        _logger.LogDebug("Discovered Stone: {Name} at {Endpoint}", 
                            stone.StoneName, stone.StoneEndpoint);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Per-receive timeout - continue until deadline
            }
            catch (SocketException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout on receive - continue until deadline
                await Task.Delay(50, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug("Invalid discovery response: {Error}", ex.Message);
            }
        }
        
        _logger.LogInformation("Discovery complete: {Count} Stone(s) found", stones.Count);
        return stones.Values.ToList();
    }

    /// <inheritdoc />
    public async Task<ResolvedService?> FindServiceAsync(
        string offering,
        CancellationToken cancellationToken = default)
    {
        // Level 1: Check offering cache
        if (_offeringCache.TryGetValue(offering, out var cached))
        {
            _logger.LogDebug("Returning cached {Offering} → {ConnectionString}", 
                offering, cached.ConnectionString);
            return cached;
        }
        
        // Level 2: Ensure we have discovered stones
        if (_topologyCache.IsEmpty)
        {
            await DiscoverStonesAsync(cancellationToken: cancellationToken);
        }
        
        if (_topologyCache.IsEmpty)
        {
            _logger.LogWarning("No Stones available in Garden");
            return null;
        }
        
        // Level 3: Search across all known Stones for the offering
        foreach (var stone in _topologyCache.Values)
        {
            if (!await IsStoneHealthyAsync(stone, TimeSpan.FromSeconds(2), cancellationToken))
            {
                _logger.LogDebug("Stone {Stone} is unhealthy, skipping", stone.StoneName);
                continue;
            }
            
            var service = await GetServiceAsync(stone, offering, cancellationToken);
            
            // Service must be running and have either Connection or Ports info
            if (service is { IsRunning: true } && (service.Connection != null || service.Ports?.Native != null))
            {
                _logger.LogDebug("Found {Offering} on {Stone}", offering, stone.StoneName);
                
                // Bind to this stone for future requests
                lock (_bindLock)
                {
                    _boundStone = stone;
                }
                
                return CacheOffering(service, stone);
            }
        }
        
        _logger.LogWarning("Service {Offering} not found on any Stone", offering);
        return null;
    }

    /// <inheritdoc />
    public async Task<ServiceInfo?> GetServiceAsync(
        Stone stone,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var url = $"{stone.StoneEndpoint}{string.Format(Constants.Moss.ServiceEndpointTemplate, Uri.EscapeDataString(serviceName))}";
        _logger.LogDebug("Querying service: {Url}", url);
        
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            _logger.LogDebug("Response status: {Status}", response.StatusCode);
            
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
                
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Response JSON: {Json}", json);
            
            var wrapper = JsonSerializer.Deserialize<ApiResponse<ServiceInfo>>(json, _jsonOptions);
            _logger.LogDebug("Parsed service: Status={Status}, HasConnection={HasConn}", 
                wrapper?.Data?.Status, wrapper?.Data?.Connection != null);
            
            return wrapper?.Data;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("Failed to get service {Service} from {Stone}: {Error}", 
                serviceName, stone.StoneName, ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(
        Stone stone,
        CancellationToken cancellationToken = default)
    {
        var url = $"{stone.StoneEndpoint}{Constants.Moss.ServicesEndpoint}";
        
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var wrapper = JsonSerializer.Deserialize<ServicesListResponse>(json, _jsonOptions);
            
            return wrapper?.Data?.Services ?? Array.Empty<ServiceInfo>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("Failed to get services from {Stone}: {Error}", stone.StoneName, ex.Message);
            return Array.Empty<ServiceInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsStoneHealthyAsync(
        Stone stone,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(2);
        
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);
            
            var url = $"{stone.StoneEndpoint}{Constants.Moss.HealthEndpoint}";
            var response = await _httpClient.GetAsync(url, cts.Token);
            
            if (!response.IsSuccessStatusCode)
                return false;
            
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var health = JsonSerializer.Deserialize<HealthResponse>(json, _jsonOptions);
            
            return health?.IsHealthy ?? false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug("Health check failed for {Stone}: {Error}", stone.StoneName, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public void InvalidateOffering(string offering)
    {
        if (_offeringCache.TryRemove(offering, out var removed))
        {
            _logger.LogInformation(
                "Invalidated offering {Offering} (was {ConnectionString}) - will re-query on next request",
                offering, removed.ConnectionString);
        }
    }

    /// <inheritdoc />
    public void InvalidateStone()
    {
        lock (_bindLock)
        {
            if (_boundStone != null)
            {
                _logger.LogInformation(
                    "Invalidated Stone binding (was {Stone}) - will re-discover on next request",
                    _boundStone.StoneName);
                _boundStone = null;
                _offeringCache.Clear();
            }
        }
    }

    /// <summary>
    /// Ensures we have a bound Stone, discovering one if needed.
    /// Uses parallel strategy: races cached topology against fresh UDP discovery.
    /// </summary>
    private async Task<Stone?> EnsureBoundStoneAsync(CancellationToken cancellationToken)
    {
        // Fast path: already bound and healthy?
        var current = _boundStone;
        if (current != null)
        {
            if (await IsStoneHealthyAsync(current, TimeSpan.FromSeconds(2), cancellationToken))
            {
                return current;
            }
            
            _logger.LogInformation("Bound Stone {Stone} unreachable, discovering new Stone...", 
                current.StoneName);
            
            lock (_bindLock)
            {
                _boundStone = null;
            }
        }
        
        // Parallel discovery: race cached topology against fresh UDP discovery
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        var cachedTask = FindHealthyCachedStoneAsync(cts.Token);
        var discoveryTask = DiscoverHealthyStoneAsync(cts.Token);
        
        var winner = await Task.WhenAny(cachedTask, discoveryTask);
        var stone = await winner;
        
        if (stone != null)
        {
            await cts.CancelAsync();
            return BindToStone(stone);
        }
        
        // First completed with null, wait for the other
        var other = winner == cachedTask ? discoveryTask : cachedTask;
        try
        {
            stone = await other;
            if (stone != null)
            {
                return BindToStone(stone);
            }
        }
        catch (OperationCanceledException) { }
        
        _logger.LogWarning("No healthy Stones found in Garden");
        return null;
    }
    
    private async Task<Stone?> FindHealthyCachedStoneAsync(CancellationToken cancellationToken)
    {
        foreach (var stone in _topologyCache.Values)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            if (await IsStoneHealthyAsync(stone, TimeSpan.FromSeconds(1), cancellationToken))
            {
                _logger.LogDebug("Found healthy Stone in topology cache: {Stone}", stone.StoneName);
                return stone;
            }
        }
        return null;
    }
    
    private async Task<Stone?> DiscoverHealthyStoneAsync(CancellationToken cancellationToken)
    {
        var stones = await DiscoverStonesAsync(cancellationToken: cancellationToken);
        
        foreach (var stone in stones)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            if (await IsStoneHealthyAsync(stone, TimeSpan.FromSeconds(2), cancellationToken))
            {
                return stone;
            }
        }
        return null;
    }
    
    private Stone BindToStone(Stone stone)
    {
        lock (_bindLock)
        {
            _boundStone = stone;
        }
        _logger.LogInformation("Bound to Stone: {Stone} at {Endpoint}", 
            stone.StoneName, stone.StoneEndpoint);
        return stone;
    }
    
    private ResolvedService CacheOffering(ServiceInfo service, Stone stone)
    {
        // Get the appropriate scheme for this offering
        var scheme = GetSchemeForOffering(service.Offering);
        string connectionString;
        
        if (service.Connection != null)
        {
            // Use the Connection object from /api/v1/services (list endpoint)
            connectionString = service.Connection.GetUri(scheme);
        }
        else if (service.Ports?.Native != null)
        {
            // Build connection from Stone host + Ports from /api/v1/services/{name} endpoint
            connectionString = $"{scheme}://{stone.Host}:{service.Ports.Native}";
        }
        else
        {
            // Fallback to default port for known offerings
            var defaultPort = GetDefaultPort(service.Offering);
            connectionString = $"{scheme}://{stone.Host}:{defaultPort}";
        }
        
        // For MongoDB, ensure the scheme is mongodb://
        if (service.Offering.Equals("mongodb", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase))
        {
            // Extract host:port and rebuild
            var uri = new Uri(connectionString.Replace("tcp://", "http://"));
            connectionString = $"mongodb://{uri.Host}:{uri.Port}";
        }
        
        var resolved = new ResolvedService
        {
            Service = service,
            Stone = stone,
            ConnectionString = connectionString
        };
        
        _offeringCache[service.Offering] = resolved;
        _logger.LogInformation("Cached offering {Offering} → {ConnectionString}", 
            service.Offering, resolved.ConnectionString);
        
        return resolved;
    }
    
    private string GetSchemeForOffering(string offering)
    {
        // Check custom mappings first
        if (_options.SchemeMappings?.TryGetValue(offering, out var customScheme) == true)
        {
            return customScheme;
        }
        
        // Fall back to built-in mappings
        if (Constants.SchemeMapping.TryGetValue(offering, out var scheme))
        {
            return scheme;
        }
        
        // Default to tcp
        return "tcp";
    }
    
    private static int GetDefaultPort(string offering)
    {
        return offering.ToLowerInvariant() switch
        {
            "mongodb" => Constants.DefaultPorts.MongoDB,
            "redis" => Constants.DefaultPorts.Redis,
            "postgresql" or "postgres" => Constants.DefaultPorts.PostgreSQL,
            "rabbitmq" => Constants.DefaultPorts.RabbitMQ,
            "mariadb" or "mysql" => Constants.DefaultPorts.MariaDB,
            "sqlserver" or "mssql" => Constants.DefaultPorts.SQLServer,
            "memcached" => Constants.DefaultPorts.Memcached,
            "vault" => Constants.DefaultPorts.Vault,
            "ollama" => Constants.DefaultPorts.Ollama,
            _ => 8080 // Generic fallback
        };
    }
    
    /// <summary>
    /// Find the best LAN interface IP for UDP binding.
    /// On multi-homed Windows systems (WSL, Hyper-V), binding to 0.0.0.0
    /// may route traffic through virtual interfaces that can't reach the LAN.
    /// </summary>
    private static IPAddress? GetLanBindAddress()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            
            foreach (var iface in interfaces)
            {
                var props = iface.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    
                    var ip = addr.Address.ToString();
                    
                    // Prefer common LAN address ranges
                    if (ip.StartsWith("192.168.") || 
                        ip.StartsWith("10.") ||
                        (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out var second) && second >= 16 && second <= 31))
                    {
                        return addr.Address;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors - will fall back to Any
        }
        
        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
