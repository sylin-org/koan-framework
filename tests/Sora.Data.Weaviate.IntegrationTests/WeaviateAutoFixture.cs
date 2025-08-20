using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Sora.Testing;
using Xunit;

namespace Sora.Data.Weaviate.IntegrationTests;

public sealed class WeaviateAutoFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    private TestcontainersContainer? _container;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        var probe = await DockerEnvironment.ProbeAsync();
        if (!probe.Available)
        {
            Available = false;
            return;
        }

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("semitechnologies/weaviate:1.24.12")
            .WithName("sora-weaviate-test")
            .WithPortBinding(8085, 8080)
            .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8080));

        _container = builder.Build();
        await _container.StartAsync();
        Available = true;
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
