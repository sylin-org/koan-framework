using Koan.Jobs.Infrastructure;

namespace Koan.Jobs.Core.Tests.Specs.Telemetry;

public class JobsTelemetrySpec
{
    [Fact(DisplayName = "JobsTelemetry: ActivitySource has correct name")]
    public void ActivitySource_has_correct_name()
    {
        JobsTelemetry.Source.Name.Should().Be("Koan.Jobs");
    }

    [Fact(DisplayName = "JobsTelemetry: ActivitySource has correct version")]
    public void ActivitySource_has_correct_version()
    {
        JobsTelemetry.Source.Version.Should().Be("0.6.3");
    }
}
