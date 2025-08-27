using FluentAssertions;
using Sora.Orchestration;
using Sora.Orchestration.Renderers.Compose;
using Xunit;

// Define a test-only adapter marker with attributes so ComposeExporter can discover mount mappings
[DefaultEndpoint("postgres", "localhost", 5432, "tcp", new[] { "test/postgres" })]
[HostMount("/var/lib/postgresql/data")]
internal sealed class TestPostgresAdapterMarker { }

public class ComposeExporterMountTests
{
    [Fact]
    public async Task Injects_bind_mount_when_attribute_present()
    {
        var plan = new Plan(
            Profile.Local,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "test/postgres:16",
                    Env: new Dictionary<string,string?>(),
                    Ports: new List<(int,int)>{ (5432,5432) },
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
        yaml.Should().Contain("services:")
            .And.Contain("db:")
            .And.Contain("image: \"test/postgres:16\"")
            .And.Contain("volumes:")
            .And.Contain("- ./Data/db:/var/lib/postgresql/data");
    }

    [Fact]
    public async Task Does_not_duplicate_mount_when_target_already_present()
    {
        var plan = new Plan(
            Profile.Local,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "test/postgres:16",
                    Env: new Dictionary<string,string?>(),
                    Ports: new List<(int,int)>(),
                    Volumes: new List<(string,string,bool)>{ ("./Data/db", "/var/lib/postgresql/data", false) },
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
        // Expect only one mapping line to the target path
        var occurrences = yaml.Split('\n').Count(l => l.Contains("/var/lib/postgresql/data"));
        occurrences.Should().Be(1);
    }
}
