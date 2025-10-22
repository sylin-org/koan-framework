using Koan.TestPipeline;

namespace Koan.Web.Admin.Tests.Specs;

public class AdminControllerSpec : IClassFixture<WebAdminTestPipelineFixture>
{
    private readonly WebAdminTestPipelineFixture _fixture;

    public AdminControllerSpec(WebAdminTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Admin: Entities controller returns expected models")]
    public async Task EntitiesController_ReturnsExpectedModels()
    {
    var response = await _fixture.HttpGetAsync("/admin/entities");
    response.Should().NotBeNull();
    response.StatusCode.Should().Be(200);
    var json = await response.Content.ReadAsStringAsync();
    json.Should().Contain("entities");
    }

    [Fact(DisplayName = "Admin: Models controller returns expected schema")]
    public async Task ModelsController_ReturnsExpectedSchema()
    {
    var response = await _fixture.HttpGetAsync("/admin/models");
    response.Should().NotBeNull();
    response.StatusCode.Should().Be(200);
    var json = await response.Content.ReadAsStringAsync();
    json.Should().Contain("schema");
    }
}
