using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace S2.Api.IntegrationTests;

public sealed class MongoFixture : Xunit.IAsyncLifetime
{
    private TestcontainersContainer? _container;
    public string ConnectionString => $"mongodb://localhost:{_hostPort}";
    private readonly int _hostPort;
    public bool Available { get; private set; }
    public MongoFixture()
    {
        // Use a non-conflicting high port for local CI
        _hostPort = 5055;
    }
    public async Task InitializeAsync()
    {
        try
        {
            _container = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage("mongo:7")
                .WithPortBinding(_hostPort, 27017)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(27017))
                .Build();
            await _container.StartAsync();
            Available = true;
        }
        catch
        {
            Available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            try { await _container.StopAsync(); } catch { }
            try { await _container.DisposeAsync(); } catch { }
        }
    }
}