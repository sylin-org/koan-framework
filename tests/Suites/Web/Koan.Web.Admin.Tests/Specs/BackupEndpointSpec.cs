using Koan.TestPipeline;

namespace Koan.Web.Admin.Tests.Specs;

public class BackupEndpointSpec : IClassFixture<WebAdminTestPipelineFixture>
{
    private readonly WebAdminTestPipelineFixture _fixture;

    public BackupEndpointSpec(WebAdminTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Web: Backup endpoint returns expected data")]
    public async Task BackupEndpoint_ReturnsExpectedData()
    {
    var response = await _fixture.HttpGetAsync("/admin/backup");
    response.Should().NotBeNull();
    response.StatusCode.Should().Be(200);
    var content = await response.Content.ReadAsStringAsync();
    content.Should().NotBeNullOrWhiteSpace();
    }
}
