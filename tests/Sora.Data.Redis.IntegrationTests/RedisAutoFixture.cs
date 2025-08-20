using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Testing;
using Sora.Data.Redis;
using Xunit;

namespace Sora.Data.Redis.IntegrationTests;

public sealed class RedisAutoFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    private TestcontainersContainer? _container;
    private bool _available;
    private string? _dockerEndpoint;

    public async Task InitializeAsync()
    {
        // If an env var is supplied, use it and skip container
        var explicitCs = Environment.GetEnvironmentVariable("SORA_REDIS__CONNECTION_STRING")
                      ?? Environment.GetEnvironmentVariable("REDIS_URL")
                      ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitCs))
        {
            ConnectionString = explicitCs;
            return;
        }

        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        var probe = await DockerEnvironment.ProbeAsync();
        if (!probe.Available) { _available = false; return; }
        _dockerEndpoint = probe.Endpoint;

        // Start a Redis container
        _container = new TestcontainersBuilder<TestcontainersContainer>()
            .WithDockerEndpoint(_dockerEndpoint)
            .WithImage("redis:7-alpine")
            .WithCleanUp(true)
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();

        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(6379);
        ConnectionString = $"localhost:{hostPort}";
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            try { await _container.StopAsync(); } catch { }
            try { await _container.DisposeAsync(); } catch { }
        }
    }
}
