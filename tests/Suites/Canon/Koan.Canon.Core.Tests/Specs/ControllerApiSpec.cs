using Koan.TestPipeline;

namespace Koan.Canon.Core.Tests.Specs;

public class ControllerApiSpec : IClassFixture<CanonCoreTestPipelineFixture>
{
    private readonly CanonCoreTestPipelineFixture _fixture;

    public ControllerApiSpec(CanonCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Canon: Controller/API specs are correct")]
    public async Task ControllerApiSpecs_AreCorrect()
    {
    // Arrange
    var api = _fixture.GetControllerApi();

    // Act
    var response = await api.PingAsync();

    // Assert
    response.Should().Be("pong");
    }
}
