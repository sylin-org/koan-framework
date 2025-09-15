using FluentAssertions;
using Koan.Orchestration;
using Koan.Orchestration.Provider.Docker;
using Xunit;

public class DockerProviderTests
{
    [Fact]
    public async Task Availability_probe_does_not_throw()
    {
        var p = new DockerProvider();
        var (ok, reason) = await p.IsAvailableAsync();
        // ok may be false on hosts without Docker; just ensure no exceptions and a stable shape
        reason?.GetType().Should().Be(typeof(string));
        var info = p.EngineInfo();
        info.Name.Should().Be("Docker");
    }

    [Fact]
    public async Task Status_returns_stable_shape_even_without_engine()
    {
        var p = new DockerProvider();
        var status = await p.Status(new StatusOptions(Service: null));
        status.Provider.Should().Be("docker");
        status.Services.Should().NotBeNull();
        if (status.Services.Count > 0)
            status.Services.Should().OnlyContain(s => !string.IsNullOrEmpty(s.Service));
    }

    [Fact(Timeout = 2000)]
    public async Task Logs_iterator_can_be_cancelled_quickly()
    {
        var p = new DockerProvider();
        using var cts = new CancellationTokenSource(200);
        // Service may not exist; we only verify that the iterator is cancellable and does not hang.
        var enumerated = false;
        try
        {
            await foreach (var line in p.Logs(new LogsOptions(Service: null, Follow: false, Tail: 1), cts.Token))
            {
                enumerated = true;
                break; // we don't need actual output
            }
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
        catch
        {
            // acceptable if docker is missing; iterator should fail fast
        }
        enumerated.Should().BeFalse();
    }

    [Fact]
    public void ParseComposePsJson_happy_path()
    {
        var json = "[ { \"Name\": \"api\", \"State\": \"running\", \"Health\": \"healthy\" }, { \"Name\": \"db\", \"State\": \"exited\" } ]";
        var items = DockerProvider.ParseComposePsJson(json);
        items.Should().HaveCount(2);
        items[0].Service.Should().Be("api");
        items[0].State.Should().Be("running");
        items[0].Health.Should().Be("healthy");
        items[1].Service.Should().Be("db");
        items[1].State.Should().Be("exited");
        items[1].Health.Should().BeNull();
    }

    [Fact]
    public void ParseComposePsJson_malformed_input_returns_empty()
    {
        var items = DockerProvider.ParseComposePsJson("not json");
        items.Should().BeEmpty();
    }
}
