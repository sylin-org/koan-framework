using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;

namespace Koan.Testing.Fixtures;

public sealed class RedisContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const int RedisPort = 6379;
    private const string DefaultDockerFixtureKey = "docker";
    private const string RyukVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private TestcontainersContainer? _container;

    public RedisContainerFixture(string dockerFixtureKey = DefaultDockerFixtureKey)
    {
        DockerFixtureKey = dockerFixtureKey;
    }

    public string DockerFixtureKey { get; }

    public bool IsAvailable { get; private set; }

    public string? ConnectionString { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsAvailable)
        {
            return;
        }

        context.Diagnostics.Info("redis.fixture.initialize", new { dockerKey = DockerFixtureKey });

        if (TryGetExplicitConnectionString(out var explicitConnection))
        {
            ConnectionString = explicitConnection;
            IsAvailable = true;
            context.Diagnostics.Info("redis.fixture.explicit", new { source = "env" });
            return;
        }

        if (await CanTcpConnectAsync("localhost", RedisPort, context.Cancellation).ConfigureAwait(false))
        {
            ConnectionString = $"localhost:{RedisPort}";
            IsAvailable = true;
            context.Diagnostics.Info("redis.fixture.local", new { host = "localhost", port = RedisPort });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingRedisContainer().";
            context.Diagnostics.Warn("redis.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("redis.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        Environment.SetEnvironmentVariable(RyukVariable, "true");
        context.Diagnostics.Debug("redis.fixture.ryuk.disabled", new { variable = RyukVariable });

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("redis:7-alpine")
            .WithCleanUp(true)
            .WithPortBinding(RedisPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(RedisPort));

        var endpoint = dockerFixture!.Endpoint;
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            builder = builder.WithDockerEndpoint(endpoint);
        }

        _container = builder.Build();
        context.Diagnostics.Info("redis.fixture.container.create", new { image = "redis:7-alpine", endpoint });

        try
        {
            await _container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = _container.GetMappedPublicPort(RedisPort);
            ConnectionString = $"localhost:{mappedPort}";
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("redis.fixture.container.started", new { host = "localhost", port = mappedPort });
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Redis container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("redis.fixture.container.failed", new { message = ex.Message }, ex);
            await DisposeContainerSilentlyAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeContainerSilentlyAsync().ConfigureAwait(false);
    }

    private static bool TryGetExplicitConnectionString(out string? connectionString)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("Koan_REDIS__CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("REDIS_URL"),
            Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
        };

        connectionString = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static async Task<bool> CanTcpConnectAsync(string host, int port, CancellationToken cancellation, int timeoutMs = 250)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cancellation)).ConfigureAwait(false);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async ValueTask DisposeContainerSilentlyAsync()
    {
        if (_container is null)
        {
            return;
        }

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

        _container = null;
    }
}
