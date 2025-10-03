using FluentAssertions;
using Koan.Orchestration;
using Koan.Orchestration.Models;
using Koan.Orchestration.Renderers.Connector.Compose;
using Xunit;

public class ComposeExporterMoreTests
{
    [Fact]
    public async Task Generates_multi_service_yaml_with_expected_shapes()
    {
        var plan = new Plan(
            Profile.Local,
            new[]
            {
                new ServiceSpec(
                    Id: "db",
                    Image: "postgres:16",
                    Env: new Dictionary<string,string?>
                    {
                        ["POSTGRES_USER"] = "Koan",
                        ["POSTGRES_PASSWORD"] = "pw",
                    },
                    Ports: new List<(int,int)>{ (5432,5432) },
                    Volumes: new List<(string,string,bool)>{ ("pgdata", "/var/lib/postgresql/data", true) },
                    Health: new HealthSpec("http://localhost:5432/", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 10),
                    Type: null,
                    DependsOn: Array.Empty<string>()
                ),
                new ServiceSpec(
                    Id: "api",
                    Image: "busybox:latest",
                    Env: new Dictionary<string,string?>{ ["MESSAGE"] = "hello" },
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
        // Core top-level and service keys
        yaml.Should().Contain("services:")
            .And.Contain("db:")
            .And.Contain("api:")
            .And.Contain("image: \"postgres:16\"")
            .And.Contain("image: \"busybox:latest\"")
            .And.Contain("environment:")
            .And.Contain("POSTGRES_USER: \"Koan\"")
            .And.Contain("POSTGRES_PASSWORD: \"pw\"")
            .And.Contain("MESSAGE: \"hello\"")
            .And.Contain("ports:")
            .And.Contain("\"5432:5432\"")
            .And.Contain("volumes:")
            .And.Contain("pgdata:")
            .And.Contain("healthcheck:")
            .And.Contain("interval: 5s")
            .And.Contain("timeout: 2s")
            .And.Contain("retries: 10")
            .And.Contain("depends_on:")
            .And.Contain("condition: service_healthy");
    }
}

