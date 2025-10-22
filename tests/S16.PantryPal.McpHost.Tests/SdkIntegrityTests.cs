using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace S16.PantryPal.McpHost.Tests;

public class SdkIntegrityTests : IClassFixture<DockerMcpHostFixture>
{
    private readonly DockerMcpHostFixture _fx;
    public SdkIntegrityTests(DockerMcpHostFixture fx) => _fx = fx;

    [Fact]
    public async Task SdkDefinitions_HasFooter_And_ValidHash()
    {
        if (Environment.GetEnvironmentVariable("S16_MCPHOST_DOCKER_UNAVAILABLE") == "1")
            return; // silently skip; fixture left flag
        var text = await _fx.Client.GetStringAsync("/mcp/sdk/definitions");
        text.Should().Contain("// integrity-sha256:");

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var footerIndex = Array.FindLastIndex(lines, l => l.StartsWith("// integrity-sha256:"));
        footerIndex.Should().BeGreaterThan(0, "footer must be present after content");

        var footer = lines[footerIndex];
        var hash = footer.Split(':', 2)[1].Trim();
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().MatchRegex("^[a-f0-9]{64}$");

        var content = string.Join('\n', lines.Take(footerIndex));
        using var sha = SHA256.Create();
        var computed = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        computed.Should().Be(hash, "hash must match content segment prior to footer");
    }
}
