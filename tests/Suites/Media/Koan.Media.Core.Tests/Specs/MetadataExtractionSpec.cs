using Koan.TestPipeline;

namespace Koan.Media.Core.Tests.Specs;

public class MetadataExtractionSpec : IClassFixture<MediaCoreTestPipelineFixture>
{
    private readonly MediaCoreTestPipelineFixture _fixture;

    public MetadataExtractionSpec(MediaCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Media: Metadata extraction is correct")]
    public async Task MetadataExtraction_IsCorrect()
    {
    // Arrange
    var media = _fixture.GetMediaProvider();
    var file = _fixture.CreateTestMediaFile("input.jpg");

    // Act
    var metadata = await media.ExtractMetadataAsync(file);

    // Assert
    metadata.Should().ContainKey("width");
    metadata.Should().ContainKey("height");
    }
}
