using FluentAssertions;
using Sora.Orchestration;
using Xunit;

namespace Sora.Orchestration.Renderers.Compose.Tests;

public class EndpointFormatterTests
{
    [Theory]
    [InlineData(null, 8080, 80, "http://localhost:8080 -> 80 (tcp)")]
    [InlineData("0.0.0.0", 3000, 3000, "http://localhost:3000 -> 3000 (tcp)")]
    [InlineData("::", 5000, 5000, "http://localhost:5000 -> 5000 (tcp)")]
    [InlineData("[::]", 5000, 5000, "http://localhost:5000 -> 5000 (tcp)")]
    [InlineData("127.0.0.1", 9200, 9200, "http://127.0.0.1:9200 -> 9200 (tcp)")]
    [InlineData("::1", 8080, 80, "http://[::1]:8080 -> 80 (tcp)")]
    [InlineData("10.0.0.5", 443, 443, "https://10.0.0.5:443 -> 443 (tcp)")]
    public void Formats_live_endpoint(string? addr, int host, int container, string expected)
    {
        var p = new PortBinding("svc", host, container, "tcp", addr);
        var s = Sora.Orchestration.Cli.Formatting.EndpointFormatter.FormatLiveEndpoint(p);
        s.Should().Be(expected);
    }

    [Theory]
    // http/https ports without image hints
    [InlineData(null, 80, "http")]
    [InlineData(null, 8080, "http")]
    [InlineData(null, 3000, "http")]
    [InlineData(null, 5000, "http")]
    [InlineData(null, 5050, "http")]
    [InlineData(null, 4200, "http")]
    [InlineData(null, 443, "https")]
    [InlineData(null, 9200, "http")]
    [InlineData(null, 12345, "tcp")]
    // image-driven schemes
    [InlineData("postgres", 5432, "postgres")]
    [InlineData("redis", 6379, "redis")]
    [InlineData("mongo", 27017, "mongodb")]
    public void Plan_hint_uses_expected_scheme_with_image_or_port(string? image, int containerPort, string scheme)
    {
        var imageOrSvc = image ?? "svc";
        var hint = Sora.Orchestration.Cli.Formatting.EndpointFormatter.GetPlanHint(imageOrSvc, containerPort, 4242);
        hint.Should().Be($"{scheme}://localhost:4242");
    }
}
