using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Koan.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Services.ServiceMesh;

/// <summary>
/// UDP multicast-based service mesh for discovery and announcement.
/// Implements three-tier architecture:
/// - Tier 1: Orchestrator channel (global discovery)
/// - Tier 2: Service channels (optional per-service pub/sub)
/// - Tier 3: HTTP endpoints (request/response)
/// </summary>
internal class KoanServiceMesh : IKoanServiceMesh, IDisposable
{
    private readonly ILogger<KoanServiceMesh> _logger;
    private readonly KoanServiceDescriptor _descriptor;
    private readonly string _instanceId;

    // Service registry
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ServiceInstance>> _services = new();

    // UDP clients for orchestrator channel
    private UdpClient? _orchestratorReceiver;
    private UdpClient? _orchestratorSender;
    private readonly IPEndPoint _orchestratorEndpoint;

    // UDP clients for service-specific channel (optional)
    private UdpClient? _serviceReceiver;
    private UdpClient? _serviceSender;
    private IPEndPoint? _serviceEndpoint;

    // Load balancing state
    private readonly ConcurrentDictionary<string, int> _roundRobinCounters = new();
    private readonly Random _random = new();

    private bool _disposed;

    public KoanServiceMesh(
        KoanServiceDescriptor descriptor,
        ILogger<KoanServiceMesh> logger)
    {
        _descriptor = descriptor;
        _logger = logger;
        _instanceId = Guid.NewGuid().ToString("N");

        // Setup orchestrator channel endpoint
        _orchestratorEndpoint = new IPEndPoint(
            IPAddress.Parse(descriptor.OrchestratorMulticastGroup),
            descriptor.OrchestratorMulticastPort);

        // Setup service-specific channel if enabled
        if (descriptor.EnableServiceChannel &&
            descriptor.ServiceMulticastGroup != null &&
            descriptor.ServiceMulticastPort.HasValue)
        {
            _serviceEndpoint = new IPEndPoint(
                IPAddress.Parse(descriptor.ServiceMulticastGroup),
                descriptor.ServiceMulticastPort.Value);
        }

        InitializeUdpClients();
    }

    private void InitializeUdpClients()
    {
        try
        {
            // Orchestrator receiver (listen for announcements from all services)
            _orchestratorReceiver = new UdpClient(_descriptor.OrchestratorMulticastPort);
            _orchestratorReceiver.JoinMulticastGroup(
                IPAddress.Parse(_descriptor.OrchestratorMulticastGroup));

            // Orchestrator sender
            _orchestratorSender = new UdpClient();
            _orchestratorSender.JoinMulticastGroup(
                IPAddress.Parse(_descriptor.OrchestratorMulticastGroup));

            _logger.LogInformation(
                "Koan:services:mesh joined orchestrator channel {Group}:{Port}",
                _descriptor.OrchestratorMulticastGroup,
                _descriptor.OrchestratorMulticastPort);

            // Service-specific channel (optional)
            if (_serviceEndpoint != null)
            {
                _serviceReceiver = new UdpClient(_serviceEndpoint.Port);
                _serviceReceiver.JoinMulticastGroup(_serviceEndpoint.Address);

                _serviceSender = new UdpClient();
                _serviceSender.JoinMulticastGroup(_serviceEndpoint.Address);

                _logger.LogInformation(
                    "Koan:services:mesh joined service channel {Group}:{Port}",
                    _descriptor.ServiceMulticastGroup,
                    _descriptor.ServiceMulticastPort);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Koan:services:mesh failed to initialize UDP clients");
            throw;
        }
    }

    public async Task AnnounceAsync(CancellationToken ct = default)
    {
        if (_orchestratorSender == null)
        {
            _logger.LogWarning("Koan:services:mesh orchestrator sender not initialized");
            return;
        }

        var announcement = new ServiceAnnouncement
        {
            MessageType = "announcement",
            InstanceId = _instanceId,
            ServiceId = _descriptor.ServiceId,
            HttpEndpoint = $"http://{GetLocalIpAddress()}:{_descriptor.Port}",
            ServiceChannelEndpoint = _serviceEndpoint != null
                ? $"{_serviceEndpoint.Address}:{_serviceEndpoint.Port}"
                : null,
            Capabilities = _descriptor.Capabilities,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(announcement);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _orchestratorSender.SendAsync(bytes, bytes.Length, _orchestratorEndpoint);

        _logger.LogDebug(
            "Koan:services:mesh announced {ServiceId} instance {InstanceId}",
            _descriptor.ServiceId,
            _instanceId);
    }

    public async Task DiscoverAsync(CancellationToken ct = default)
    {
        if (_orchestratorSender == null)
        {
            _logger.LogWarning("Koan:services:mesh orchestrator sender not initialized");
            return;
        }

        var discovery = new ServiceDiscoveryRequest
        {
            MessageType = "discovery",
            RequesterId = _instanceId,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(discovery);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _orchestratorSender.SendAsync(bytes, bytes.Length, _orchestratorEndpoint);

        _logger.LogInformation("Koan:services:mesh broadcast discovery request");
    }

    public async Task MaintainAsync(CancellationToken ct = default)
    {
        if (_orchestratorReceiver == null)
        {
            _logger.LogWarning("Koan:services:mesh orchestrator receiver not initialized");
            return;
        }

        _logger.LogInformation("Koan:services:mesh maintenance started");

        // Start listening task
        var listenTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _orchestratorReceiver.ReceiveAsync(ct);
                    var json = Encoding.UTF8.GetString(result.Buffer);

                    await ProcessMessageAsync(json, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Koan:services:mesh error receiving message");
                }
            }
        }, ct);

        // Start cleanup task
        var cleanupTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_descriptor.StaleThreshold / 2, ct);
                    CleanStaleInstances();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);

        await Task.WhenAll(listenTask, cleanupTask);

        _logger.LogInformation("Koan:services:mesh maintenance stopped");
    }

    private async Task ProcessMessageAsync(string json, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("MessageType", out var messageTypeProp))
                return;

            var messageType = messageTypeProp.GetString();

            switch (messageType)
            {
                case "announcement":
                    ProcessAnnouncement(json);
                    break;

                case "discovery":
                    // Respond to discovery requests with our announcement
                    await AnnounceAsync(ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Koan:services:mesh error processing message: {Json}", json);
        }
    }

    private void ProcessAnnouncement(string json)
    {
        try
        {
            var announcement = JsonSerializer.Deserialize<ServiceAnnouncement>(json);
            if (announcement == null || announcement.InstanceId == _instanceId)
                return; // Ignore our own announcements

            var instance = new ServiceInstance
            {
                InstanceId = announcement.InstanceId,
                ServiceId = announcement.ServiceId,
                HttpEndpoint = announcement.HttpEndpoint,
                ServiceChannelEndpoint = announcement.ServiceChannelEndpoint,
                Capabilities = announcement.Capabilities,
                LastSeen = DateTime.UtcNow,
                Status = ServiceInstanceStatus.Healthy,
                DeploymentMode = ServiceDeploymentMode.Container // TODO: detect in-process vs container
            };

            var serviceInstances = _services.GetOrAdd(
                announcement.ServiceId,
                _ => new ConcurrentDictionary<string, ServiceInstance>());

            serviceInstances.AddOrUpdate(
                announcement.InstanceId,
                instance,
                (_, existing) =>
                {
                    existing.LastSeen = DateTime.UtcNow;
                    existing.HttpEndpoint = announcement.HttpEndpoint;
                    existing.ServiceChannelEndpoint = announcement.ServiceChannelEndpoint;
                    existing.Capabilities = announcement.Capabilities;
                    return existing;
                });

            _logger.LogDebug(
                "Koan:services:mesh discovered {ServiceId} instance {InstanceId} at {Endpoint}",
                announcement.ServiceId,
                announcement.InstanceId,
                announcement.HttpEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Koan:services:mesh error processing announcement");
        }
    }

    private void CleanStaleInstances()
    {
        var staleThreshold = DateTime.UtcNow - _descriptor.StaleThreshold;
        var removedCount = 0;

        foreach (var (serviceId, instances) in _services)
        {
            var staleInstances = instances
                .Where(kvp => kvp.Value.LastSeen < staleThreshold)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var instanceId in staleInstances)
            {
                if (instances.TryRemove(instanceId, out _))
                {
                    removedCount++;
                    _logger.LogInformation(
                        "Koan:services:mesh removed stale {ServiceId} instance {InstanceId}",
                        serviceId,
                        instanceId);
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug("Koan:services:mesh cleaned {Count} stale instances", removedCount);
        }
    }

    public ServiceInstance? GetInstance(
        string serviceId,
        LoadBalancingPolicy policy = LoadBalancingPolicy.RoundRobin)
    {
        if (!_services.TryGetValue(serviceId, out var instances) || instances.IsEmpty)
            return null;

        var healthyInstances = instances.Values
            .Where(i => i.Status == ServiceInstanceStatus.Healthy)
            .ToArray();

        if (healthyInstances.Length == 0)
            return null;

        return policy switch
        {
            LoadBalancingPolicy.RoundRobin => SelectRoundRobin(serviceId, healthyInstances),
            LoadBalancingPolicy.Random => SelectRandom(healthyInstances),
            LoadBalancingPolicy.LeastConnections => SelectLeastConnections(healthyInstances),
            LoadBalancingPolicy.HealthAware => SelectHealthAware(healthyInstances),
            _ => healthyInstances[0]
        };
    }

    private ServiceInstance SelectRoundRobin(string serviceId, ServiceInstance[] instances)
    {
        var counter = _roundRobinCounters.AddOrUpdate(
            serviceId,
            0,
            (_, current) => (current + 1) % instances.Length);

        return instances[counter];
    }

    private ServiceInstance SelectRandom(ServiceInstance[] instances)
    {
        lock (_random)
        {
            return instances[_random.Next(instances.Length)];
        }
    }

    private ServiceInstance SelectLeastConnections(ServiceInstance[] instances)
    {
        return instances.MinBy(i => i.ActiveConnections) ?? instances[0];
    }

    private ServiceInstance SelectHealthAware(ServiceInstance[] instances)
    {
        // Sort by response time, prefer fastest
        return instances.MinBy(i => i.AverageResponseTime) ?? instances[0];
    }

    public IReadOnlyList<ServiceInstance> GetAllInstances(string serviceId)
    {
        if (!_services.TryGetValue(serviceId, out var instances))
            return Array.Empty<ServiceInstance>();

        return instances.Values.ToArray();
    }

    public IReadOnlyList<string> GetDiscoveredServices()
    {
        return _services.Keys.ToArray();
    }

    private static string GetLocalIpAddress()
    {
        // Get local IP address for announcements
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        }

        return "127.0.0.1";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _orchestratorReceiver?.Close();
        _orchestratorSender?.Close();
        _serviceReceiver?.Close();
        _serviceSender?.Close();

        _disposed = true;
    }
}

// Message types for UDP communication

internal class ServiceAnnouncement
{
    public string MessageType { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string HttpEndpoint { get; set; } = "";
    public string? ServiceChannelEndpoint { get; set; }
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public DateTime Timestamp { get; set; }
}

internal class ServiceDiscoveryRequest
{
    public string MessageType { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
