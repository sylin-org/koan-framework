using Koan.Core.Provenance;
using Koan.Mcp.Options;
using Koan.Mcp.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Conformance.Tests;

public sealed class TransportContractSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public TransportContractSpec(ConformanceFixture fx) => _fx = fx;

    [Fact]
    public void Canonical_http_options_bind_without_transitional_aliases()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Mcp:EnableStreamableHttpTransport"] = "true",
                ["Koan:Mcp:EnableLegacySseTransport"] = "true",
                ["Koan:Mcp:HttpRoute"] = "/agent",
                ["Koan:Mcp:MaxConcurrentSessions"] = "12",
                ["Koan:Mcp:SessionIdleTimeout"] = "00:07:00"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<McpServerOptions>().Bind(configuration.GetSection("Koan:Mcp"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        options.EnableStreamableHttpTransport.Should().BeTrue();
        options.EnableLegacySseTransport.Should().BeTrue();
        options.HttpRoute.Should().Be("/agent");
        options.MaxConcurrentSessions.Should().Be(12);
        options.SessionIdleTimeout.Should().Be(TimeSpan.FromMinutes(7));

        var names = typeof(McpServerOptions).GetProperties().Select(property => property.Name).ToArray();
        names.Should().NotContain(["EnableHttpSseTransport", "StreamableHttpEnabled", "HttpSseRoute", "MaxConcurrentConnections", "SseConnectionTimeout"]);
        typeof(McpServerOptions).Assembly.GetType("Koan.Mcp.McpTransportMode").Should().BeNull();
    }

    [Fact]
    public void Startup_provenance_uses_the_same_transport_vocabulary()
    {
        _fx.Services.Should().NotBeNull("the real AddKoan host has completed module reporting");
        var keys = ProvenanceRegistry.Instance.CurrentSnapshot.Pillars
            .SelectMany(pillar => pillar.Modules)
            .SelectMany(module => module.Settings)
            .Select(setting => setting.Key)
            .ToArray();

        keys.Should().Contain("Koan:Mcp:EnableStreamableHttpTransport");
        keys.Should().Contain("Koan:Mcp:EnableLegacySseTransport");
        keys.Should().NotContain("Koan:Mcp:EnableHttpSseTransport");
        keys.Should().NotContain("Koan:Mcp:HttpSseRoute");
    }

    [Fact]
    public async Task Capability_report_describes_the_enabled_host_edge()
    {
        var reporter = _fx.Services.GetRequiredService<IMcpCapabilityReporter>();
        var report = await reporter.GetCapabilities(TestContext.Current.CancellationToken);

        report.Transports.Should().ContainSingle();
        report.Transports[0].Kind.Should().Be("streamable-http");
        report.Transports[0].Endpoint.Should().Be("/mcp");
        report.Transports[0].StreamEndpoint.Should().BeNull();
        report.Transports[0].SubmitEndpoint.Should().BeNull();
    }
}
