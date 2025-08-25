using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests.Fixtures;

[CollectionDefinition(RabbitMqCollection.Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqSharedContainer>
{
    public const string Name = "RabbitMQ(shared)";
}

public sealed class RabbitMqSharedContainer : IAsyncLifetime
{
    private TestcontainersContainer? _rabbit;
    private int _hostPort = 5674; // avoid default 5672 to reduce conflicts
    public bool Available { get; private set; }
    public string ConnectionString => $"amqp://guest:guest@localhost:{_hostPort}";
    public string ManagementUrl => $"http://localhost:{15672}";

    public async Task InitializeAsync()
    {
        // Standardized Docker probing and Ryuk disable
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        var probe = await Sora.Testing.DockerEnvironment.ProbeAsync();
        if (!probe.Available)
        {
            Available = false;
            return;
        }
        try
        {
            _rabbit = new TestcontainersBuilder<TestcontainersContainer>()
                .WithDockerEndpoint(probe.Endpoint)
                .WithImage("rabbitmq:3.13-management")
                .WithPortBinding(_hostPort, 5672)
                .WithPortBinding(15672, 15672)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
                .Build();
            await _rabbit.StartAsync();
            Available = true;
        }
        catch
        {
            Available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_rabbit != null)
        {
            try { await _rabbit.StopAsync(); } catch { }
            try { await _rabbit.DisposeAsync(); } catch { }
        }
    }
}
