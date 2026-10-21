using Koan.Core.Diagnostics;
using Koan.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

public sealed class RuntimeFactsResourceSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fixture;

    public RuntimeFactsResourceSpec(ConformanceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Runtime_facts_are_listed_and_project_the_exact_host_envelope()
    {
        var resources = await _fixture.ListResourcesAsync();
        resources.OfType<JObject>().Select(resource => resource["uri"]?.Value<string>())
            .Should().Contain(RuntimeFactsResourceProvider.ResourceUri);

        var contents = await _fixture.ReadResourceAsync(RuntimeFactsResourceProvider.ResourceUri);
        contents.Should().NotBeNull();

        var actual = contents!["text"]!.Value<string>();
        var expected = KoanFactJson.Serialize(
            _fixture.Services.GetRequiredService<IKoanRuntimeFacts>().Current);
        actual.Should().Be(expected);

        var envelope = KoanFactJson.Deserialize(actual!);
        envelope.Should().NotBeNull();
        envelope!.Schema.Should().Be(2);
        envelope!.Complete.Should().BeTrue();
    }
}

/// <summary>SEC-0018: a host outside Development that does not opt in keeps its facts off MCP, as off Web.</summary>
public sealed class StagingHostFixture : Koan.Mcp.TestKit.McpHarnessFixtureBase
{
    protected override string Environment => "Staging";
}

public sealed class RuntimeFactsGatingSpec : IClassFixture<StagingHostFixture>
{
    private readonly StagingHostFixture _fixture;

    public RuntimeFactsGatingSpec(StagingHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Runtime_facts_are_neither_listed_nor_readable_outside_development()
    {
        var resources = await _fixture.ListResourcesAsync();
        resources.OfType<JObject>().Select(resource => resource["uri"]?.Value<string>())
            .Should().NotContain(RuntimeFactsResourceProvider.ResourceUri);

        (await _fixture.ReadResourceAsync(RuntimeFactsResourceProvider.ResourceUri)).Should().BeNull();
    }
}
