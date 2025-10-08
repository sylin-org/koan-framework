using Koan.TestPipeline;

namespace Koan.Media.Core.Tests.Specs;

public class TranscodingSpec : IClassFixture<MediaCoreTestPipelineFixture>
{
    private readonly MediaCoreTestPipelineFixture _fixture;

    public TranscodingSpec(MediaCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Media: Transcoding works as expected")]
    public async Task Transcoding_WorksAsExpected()
    {
    // Arrange
    var media = _fixture.GetMediaProvider();
    var file = _fixture.CreateTestMediaFile("input.mp4");

    // Act
    var transcoded = await media.TranscodeAsync(file, "audio/mp3");

    // Assert
    transcoded.Should().NotBeNull();
    transcoded!.MimeType.Should().Be("audio/mp3");
    }
}
