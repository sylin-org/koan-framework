using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing;
using System.Net.Sockets;
using Xunit;

namespace Koan.Data.Connector.Redis.IntegrationTests;

public sealed class RedisAutoFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    private TestcontainersContainer? _container;
    private string? _dockerEndpoint;

    public async Task InitializeAsync()
    {
        // If an env var is supplied, use it and skip container
        var explicitCs = Environment.GetEnvironmentVariable("Koan_REDIS__CONNECTION_STRING")
                      ?? Environment.GetEnvironmentVariable("REDIS_URL")
                      ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitCs))
        {
            ConnectionString = explicitCs;
            return;
        }

        // Try local Redis on default port quickly
        if (await CanTcpConnectAsync("localhost", 6379))
        {
            ConnectionString = "localhost:6379";
            return;
        }

        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        var probe = await DockerEnvironment.ProbeAsync();
        if (!probe.Available)
        {
            Console.Error.WriteLine($"RedisAutoFixture: Docker unavailable. {probe.Message}");
            return;
        }
        _dockerEndpoint = probe.Endpoint;

        // Start a Redis container
        _container = new TestcontainersBuilder<TestcontainersContainer>()
            .WithDockerEndpoint(_dockerEndpoint)
            .WithImage("redis:7-alpine")
            .WithCleanUp(true)
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();

        try
        {
            await _container.StartAsync();
            var hostPort = _container.GetMappedPublicPort(6379);
            ConnectionString = $"localhost:{hostPort}";
            Console.Error.WriteLine($"RedisAutoFixture: Started redis container via {_dockerEndpoint} on localhost:{hostPort}.");
        }
        catch (Exception ex)
        {
            // On some Windows/Docker setups, attaching/hijacking can fail. Mark unavailable to skip tests.
            Console.Error.WriteLine($"RedisAutoFixture: Failed to start container. {ex}");
            ConnectionString = null;
            _container = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            try { await _container.StopAsync(); } catch { }
            try { await _container.DisposeAsync(); } catch { }
        }
    }

    private static async Task<bool> CanTcpConnectAsync(string host, int port, int timeoutMs = 250)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            return completed == connectTask && client.Connected;
        }
        catch { return false; }
    }
}

