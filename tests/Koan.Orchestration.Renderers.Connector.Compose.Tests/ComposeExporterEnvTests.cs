using FluentAssertions;
using Koan.Orchestration;
using Koan.Orchestration.Models;
using Koan.Orchestration.Renderers.Connector.Compose;
using Xunit;

public class ComposeExporterEnvTests
{
    [Fact]
    public async Task Preserves_env_substitution_unquoted_and_quotes_literals()
    {
        var plan = new Plan(
            Profile.Local,
            new[]
            {
                new ServiceSpec(
                    Id: "api",
                    Image: "busybox:latest",
                    Env: new Dictionary<string,string?>
                    {
                        ["PASSWORD"] = "${DB_PASSWORD}",
                        ["PLAIN"] = "value",
                        ["BOOLISH"] = "true",
                    },
                    Ports: Array.Empty<(int,int)>(),
                    Volumes: Array.Empty<(string,string,bool)>(),
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
        yaml.Should().Contain("PASSWORD: ${DB_PASSWORD}");
        yaml.Should().Contain("PLAIN: \"value\"");
        yaml.Should().Contain("BOOLISH: \"true\"");
    }
}

