using Xunit;
using FluentAssertions;
using Koan.Testing;
using Microsoft.Extensions.DependencyInjection;
using S16.PantryPal;

namespace Koan.Samples.PantryPal.Tests;

public class PantryPalHealthSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fixture;

    public PantryPalHealthSpec(TestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "PantryPal Service starts and responds to /health endpoint")]
    public async Task HealthEndpoint_ShouldReturnHealthy()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        content.Should().Contain("healthy");
    }
}
