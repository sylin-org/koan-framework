using Koan.TestPipeline;

namespace Koan.Media.Core.Tests.Specs;

public class FallbackSpec : IClassFixture<MediaCoreTestPipelineFixture>
{
    private readonly MediaCoreTestPipelineFixture _fixture;

    public FallbackSpec(MediaCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Media: Fallback works as expected")]
    public async Task Fallback_WorksAsExpected()
    {
    // Arrange
    var media = _fixture.GetMediaProviderWithFallback();
    var file = _fixture.CreateTestMediaFile("input.unknown");

    // Act
    var result = await media.ProcessAsync(file);

    // Assert
    result.Should().NotBeNull();
    result!.UsedFallback.Should().BeTrue();
    }
}
