using FluentAssertions;
using Sora.Orchestration;
using Sora.Orchestration.Renderers.Compose;
using Xunit;

// Test-only adapter marker for mapping
[DefaultEndpoint("postgres", "localhost", 5432, "tcp", new[] { "test/ci-postgres" })]
[HostMount("/var/lib/postgresql/data")]
internal sealed class TestCiPostgresAdapterMarker { }

public class ComposeExporterProfileMountsTests
{
    [Fact]
    public async Task CI_uses_named_volume_instead_of_bind()
    {
        var plan = new Plan(
            Profile.Ci,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "test/ci-postgres:16",
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
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        await exporter.GenerateAsync(plan, Profile.Ci, tmp);

        var yaml = await File.ReadAllTextAsync(tmp);
        yaml.Should().Contain("volumes:");
        yaml.Should().Contain("- data_db:/var/lib/postgresql/data");
        yaml.Should().Contain("data_db:");
        yaml.Should().NotContain("./Data/db:/var/lib/postgresql/data");
    }

    [Fact]
    public async Task Prod_does_not_inject_mounts()
    {
        var plan = new Plan(
            Profile.Prod,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "test/ci-postgres:16",
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
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        await exporter.GenerateAsync(plan, Profile.Prod, tmp);

        var yaml = await File.ReadAllTextAsync(tmp);
        yaml.Should().NotContain("/var/lib/postgresql/data");
        yaml.Should().NotContain("./Data/db:");
        yaml.Should().NotContain("data_db:");
    }
}
