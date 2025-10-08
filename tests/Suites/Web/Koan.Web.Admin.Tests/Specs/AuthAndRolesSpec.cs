using Koan.TestPipeline;

namespace Koan.Web.Admin.Tests.Specs;

public class AuthAndRolesSpec : IClassFixture<WebAdminTestPipelineFixture>
{
    private readonly WebAdminTestPipelineFixture _fixture;

    public AuthAndRolesSpec(WebAdminTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Web: Auth and roles regression - policies and discovery")]
    public async Task AuthAndRoles_PoliciesAndDiscovery()
    {
    var response = await _fixture.HttpGetAsync("/admin/auth/roles");
    response.Should().NotBeNull();
    response.StatusCode.Should().Be(200);
    var json = await response.Content.ReadAsStringAsync();
    json.Should().Contain("roles");
    }
}
