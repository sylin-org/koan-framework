using FluentAssertions;
using Koan.Orchestration;
using Koan.Orchestration.Models;
using Koan.Orchestration.Renderers.Connector.Compose;
using Xunit;

public class ComposeExporterTests
{
    [Fact]
    public async Task Generates_basic_compose_yaml()
    {
        var plan = new Plan(
            Profile.Local,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "postgres:16",
                    Env: new Dictionary<string,string?>{ ["POSTGRES_PASSWORD"] = "pw" },
                    Ports: new List<(int,int)>{ (5432,5432) },
                    Volumes: new List<(string,string,bool)>{ ("pgdata", "/var/lib/postgresql/data", true) },
                    Health: new HealthSpec("http://localhost:5432/", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 5),
                    Type: null,
                    DependsOn: Array.Empty<string>()
                ),
                // Minimal app that depends on db so depends_on emits condition: service_healthy
                new ServiceSpec(
                    Id: "api",
                    Image: "busybox:latest",
                    Env: new Dictionary<string,string?>(),
                    Ports: Array.Empty<(int,int)>(),
                    Volumes: Array.Empty<(string,string,bool)>(),
                    Health: null,
                    Type: null,
                    DependsOn: new[]{ "db" }
                )
            }
        );

        var exporter = new ComposeExporter();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"), "compose.yml");
        await exporter.GenerateAsync(plan, Profile.Local, tmp);

        var yaml = await File.ReadAllTextAsync(tmp);
        yaml.Should().Contain("services:")
            .And.Contain("db:")
            .And.Contain("image: \"postgres:16\"")
            .And.Contain("ports:")
            .And.Contain("\"5432:5432\"")
            .And.Contain("volumes:")
            .And.Contain("pgdata:")
            .And.Contain("environment:")
            .And.Contain("POSTGRES_PASSWORD: \"pw\"")
            .And.Contain("healthcheck:")
            .And.Contain("condition: service_healthy");
    }
}

