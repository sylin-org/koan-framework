using FluentAssertions;
using Sora.Orchestration;
using Sora.Orchestration.Models;
using Sora.Orchestration.Renderers.Compose;
using Xunit;

// Test-only adapter marker with multiple HostMounts

public class ComposeExporterMultiMountTests
{
    [Fact]
    public async Task Injects_all_declared_mount_targets_once_each()
    {
        var plan = new Plan(
            Profile.Local,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "test/multi:1",
                    Env: new Dictionary<string,string?>(),
                    Ports: new List<(int,int)>(),
                    Volumes: new List<(string,string,bool)>(),
                    Health: null,
                    Type: null,
                    DependsOn: Array.Empty<string>()
                )
            }
        );

        var exporter = new ComposeExporter();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "compose.yml");
        await exporter.GenerateAsync(plan, Profile.Local, tmp);

        var yaml = await File.ReadAllTextAsync(tmp);
        yaml.Should().Contain("- ./Data/db:/var/lib/serviceA");
        yaml.Should().Contain("- ./Data/db:/var/lib/serviceB");
        yaml.Split('\n').Count(l => l.Contains("/var/lib/serviceA")).Should().Be(1);
        yaml.Split('\n').Count(l => l.Contains("/var/lib/serviceB")).Should().Be(1);
    }

    [Fact]
    public async Task When_one_target_exists_only_missing_targets_are_added()
    {
        var plan = new Plan(
            Profile.Local,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "test/multi:1",
                    Env: new Dictionary<string,string?>(),
                    Ports: new List<(int,int)>(),
                    Volumes: new List<(string,string,bool)>{ ("./Data/db", "/var/lib/serviceA", false) },
                    Health: null,
                    Type: null,
                    DependsOn: Array.Empty<string>()
                )
            }
        );

        var exporter = new ComposeExporter();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "compose.yml");
        await exporter.GenerateAsync(plan, Profile.Local, tmp);

        var yaml = await File.ReadAllTextAsync(tmp);
        yaml.Split('\n').Count(l => l.Contains("/var/lib/serviceA")).Should().Be(1);
        yaml.Split('\n').Count(l => l.Contains("/var/lib/serviceB")).Should().Be(1);
    }
}
