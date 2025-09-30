using FluentAssertions;
using Koan.Orchestration.Connector.Podman;
using Xunit;

public class PodmanProviderPortsTests
{
    [Fact]
    public void ParseComposePsPorts_handles_string_and_array()
    {
        var json = "[ { \"Name\": \"api\", \"Ports\": \"0.0.0.0:3000->80/tcp\" }, { \"Name\": \"db\", \"Ports\": [\"127.0.0.1:15432->5432/tcp\"] } ]";
        var ports = PodmanProvider.ParseComposePsPorts(json);
        ports.Should().Contain(p => p.Service == "api" && p.Host == 3000 && p.Container == 80 && p.Protocol == "tcp");
        ports.Should().Contain(p => p.Service == "db" && p.Host == 15432 && p.Container == 5432);
    }

    [Fact]
    public void ParseComposePsPorts_ignores_malformed()
    {
        var ports = PodmanProvider.ParseComposePsPorts("not json");
        ports.Should().BeEmpty();
    }
}

