using Xunit;
using FluentAssertions;
using Koan.Testing;
using Microsoft.Extensions.DependencyInjection;
using S13.DocMind;

namespace Koan.Samples.DocMind.Tests;

public class DocMindHealthSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fixture;

    public DocMindHealthSpec(TestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "DocMind Service starts and responds to /health endpoint")]
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
