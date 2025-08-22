using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Sora.Testing;
using Xunit;

namespace Sora.Data.Weaviate.IntegrationTests;

public sealed class WeaviateAutoFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? BaseUrl { get; private set; }
    private TestcontainersContainer? _container;
    private string? _dockerEndpoint;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        var probe = await DockerEnvironment.ProbeAsync();
        if (!probe.Available)
        {
            Available = false;
            return;
        }
        _dockerEndpoint = probe.Endpoint;

        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithDockerEndpoint(_dockerEndpoint!)
            .WithImage("semitechnologies/weaviate:1.24.12")
            .WithName($"sora-weaviate-test-{Guid.NewGuid():N}")
            .WithPortBinding(8080, true) // dynamic host port
            .WithCleanUp(true)
            .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8080));

        _container = builder.Build();
        try
        {
            await _container.StartAsync();
            var hostPort = _container.GetMappedPublicPort(8080);
            BaseUrl = $"http://localhost:{hostPort}";
            Available = true;
        }
        catch
        {
            Available = false;
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
}
