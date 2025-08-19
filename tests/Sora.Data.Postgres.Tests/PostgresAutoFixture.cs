using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Xunit;

namespace Sora.Data.Postgres.Tests;

public sealed class PostgresAutoFixture : IAsyncLifetime, IDisposable
{
    private TestcontainersContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;
    public ServiceProvider ServiceProvider { get; private set; } = default!;
    public IDataService Data { get; private set; } = default!;
    public bool SkipTests { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
    // Disable Testcontainers' resource reaper to avoid Docker hijack issues on some Windows setups
    Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

    // Probe Docker daemon robustly and derive a stable endpoint string for Testcontainers
    var dockerEndpoint = await GetDockerEndpointAsync();

        var envCxn = Environment.GetEnvironmentVariable("SORA_POSTGRES__CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

        if (!string.IsNullOrWhiteSpace(envCxn))
        {
            ConnectionString = envCxn!;
        }
        else
        {
            try
            {
                var password = "postgres";
                var builder = new TestcontainersBuilder<TestcontainersContainer>()
                    .WithDockerEndpoint(dockerEndpoint)
                    .WithImage("postgres:16-alpine")
                    .WithEnvironment("POSTGRES_PASSWORD", password)
                    .WithEnvironment("POSTGRES_DB", "sora")
                    .WithPortBinding(54329, 5432)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
                    ;
                _container = builder.Build();
                await _container.StartAsync();
                ConnectionString = $"Host=localhost;Port=54329;Database=sora;Username=postgres;Password={password}";
            }
            catch (Exception ex)
            {
                SkipTests = true;
                SkipReason = $"Docker not available: {ex.GetType().Name}: {ex.Message}";
                return;
            }
        }

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>(Sora.Data.Postgres.Infrastructure.Constants.Configuration.Keys.ConnectionString, ConnectionString),
                new KeyValuePair<string,string?>(Sora.Data.Postgres.Infrastructure.Constants.Configuration.Keys.DefaultPageSize, "10"),
                new KeyValuePair<string,string?>(Sora.Data.Postgres.Infrastructure.Constants.Configuration.Keys.MaxPageSize, "50"),
                new KeyValuePair<string,string?>("Sora:Environment", "Test")
            })
            .Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(cfg);
    services.AddSoraCore();
    services.AddSoraDataCore();
    services.AddPostgresAdapter();
    ServiceProvider = services.BuildServiceProvider();
    Data = ServiceProvider.GetRequiredService<IDataService>();
    }

    private static async Task<string> GetDockerEndpointAsync()
    {
        // Prefer environment override if provided
        var env = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (await PingDockerAsync(env!)) return env!;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var npipe = "npipe://./pipe/docker_engine";
            if (await PingDockerAsync(npipe)) return npipe;
            // Some Windows setups expose Docker over TCP
            var tcp = "http://localhost:2375";
            if (await PingDockerAsync(tcp)) return tcp;
        }
        else
        {
            var unix = "unix:///var/run/docker.sock";
            if (await PingDockerAsync(unix)) return unix;
            var tcp = "http://localhost:2375";
            if (await PingDockerAsync(tcp)) return tcp;
        }

        // Fallback to default and let Testcontainers try; caller will catch exceptions
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
    }

    private static async Task<bool> PingDockerAsync(string endpoint)
    {
        try
        {
            using var client = CreateDockerClient(endpoint);
            // Simple ping
            await client.System.PingAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DockerClient CreateDockerClient(string endpoint)
    {
        DockerClientConfiguration config;
        if (endpoint.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
        {
            config = new DockerClientConfiguration(new Uri(endpoint));
        }
        else if (endpoint.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
        {
            // Docker.DotNet expects the unix socket path without the scheme in some versions, but configuration accepts the full URI
            config = new DockerClientConfiguration(new Uri(endpoint));
        }
        else
        {
            // http(s)://
            config = new DockerClientConfiguration(new Uri(endpoint));
        }
        return config.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await Task.Yield();
            ServiceProvider.Dispose();
        }
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public void Dispose() { }
}
