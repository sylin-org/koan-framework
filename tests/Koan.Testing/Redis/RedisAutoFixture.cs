using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing;
using Xunit;

namespace Koan.Testing.Redis;

public sealed class RedisAutoFixture : IAsyncLifetime
{
    private TestcontainersContainer? _container;
    private string? _dockerEndpoint;

    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            Log("InitializeAsync starting");
            await InitializeInternalAsync().ConfigureAwait(false);
            Log($"InitializeAsync completed with ConnectionString='{ConnectionString}'");
        }
        catch (TypeInitializationException ex) when (ex.InnerException is MissingMethodException)
        {
            _container = null;
            ConnectionString = null;
        }
        catch (MissingMethodException)
        {
            Log("InitializeAsync caught MissingMethodException (likely Testcontainers compatibility issue)");
            _container = null;
            ConnectionString = null;
        }
    }

    private async Task InitializeInternalAsync()
    {
        var explicitCs = Environment.GetEnvironmentVariable("Koan_REDIS__CONNECTION_STRING")
                       ?? Environment.GetEnvironmentVariable("REDIS_URL")
                       ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitCs))
        {
            Log("Using explicit connection string from environment variable");
            ConnectionString = explicitCs;
            return;
        }

        if (await CanTcpConnectAsync("localhost", 6379).ConfigureAwait(false))
        {
            Log("Detected reachable Redis on localhost:6379");
            ConnectionString = "localhost:6379";
            return;
        }

        Log("No explicit connection string and no local Redis. Attempting container startup.");
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        Log("Disabled Ryuk sidecar via TESTCONTAINERS_RYUK_DISABLED=true");

        try
        {
            var probe = await DockerEnvironment.ProbeAsync().ConfigureAwait(false);
            if (!probe.Available)
            {
                Log($"Docker unavailable. {probe.Message}");
                return;
            }

            _dockerEndpoint = probe.Endpoint;
            Log($"Docker probe succeeded. Endpoint='{_dockerEndpoint}' Detail='{probe.Message}'.");

            _container = new TestcontainersBuilder<TestcontainersContainer>()
                .WithDockerEndpoint(_dockerEndpoint)
                .WithImage("redis:7-alpine")
                .WithCleanUp(true)
                .WithPortBinding(6379, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
                .Build();

            Log("Built Testcontainers configuration. Starting container...");
            await _container.StartAsync().ConfigureAwait(false);
            var hostPort = _container.GetMappedPublicPort(6379);
            ConnectionString = $"localhost:{hostPort}";
            Log($"Started redis container via {_dockerEndpoint} on localhost:{hostPort}.");
        }
        catch (MissingMethodException)
        {
            Log("Encountered MissingMethodException while starting container");
            ConnectionString = null;
            _container = null;
        }
        catch (Exception ex)
        {
            Log($"Failed to start container. {ex.GetType().FullName}: {ex.Message}");
            if (ex.InnerException is not null)
            {
                Log($"InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
            Log($"StackTrace: {ex.StackTrace}");
            ConnectionString = null;
            _container = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            try
            {
                await _container.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            try
            {
                await _container.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static async Task<bool> CanTcpConnectAsync(string host, int port, int timeoutMs = 250)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine($"RedisAutoFixture: {message}");
    }
}
