using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace Koan.Security.Trust.IntegrationTests;

public sealed class AuthEndToEndSpec : IClassFixture<AuthE2EFixture>
{
    private readonly AuthE2EFixture _fx;
    public AuthEndToEndSpec(AuthE2EFixture fx) => _fx = fx;

    [Fact]
    public async Task Open_endpoint_serves_over_the_real_pipeline()
    {
        var response = await _fx.CreateClient().GetAsync("/e2e/open");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
