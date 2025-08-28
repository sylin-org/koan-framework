using FluentAssertions;
using Sora.Orchestration.Provider.Docker;
using Xunit;

public class DockerProviderPortsTests
{
    [Fact]
    public void ParseComposePsPorts_handles_string_and_array()
    {
        var json = "[ { \"Name\": \"api\", \"Ports\": \"0.0.0.0:8080->80/tcp, :::8443->8443/tcp\" }, { \"Name\": \"db\", \"Ports\": [\"0.0.0.0:5432->5432/tcp\"] } ]";
        var ports = DockerProvider.ParseComposePsPorts(json);
        ports.Should().Contain(p => p.Service == "api" && p.Host == 8080 && p.Container == 80 && p.Protocol == "tcp");
        ports.Should().Contain(p => p.Service == "api" && p.Host == 8443 && p.Container == 8443);
        ports.Should().Contain(p => p.Service == "db" && p.Host == 5432 && p.Container == 5432);
    }

    [Fact]
    public void ParseComposePsPorts_ignores_malformed()
    {
        var ports = DockerProvider.ParseComposePsPorts("not json");
        ports.Should().BeEmpty();
    }
}
