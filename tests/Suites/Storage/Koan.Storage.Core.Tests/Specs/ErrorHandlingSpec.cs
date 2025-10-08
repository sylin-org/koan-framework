using Koan.TestPipeline;

namespace Koan.Storage.Core.Tests.Specs;

public class ErrorHandlingSpec : IClassFixture<StorageCoreTestPipelineFixture>
{
    private readonly StorageCoreTestPipelineFixture _fixture;

    public ErrorHandlingSpec(StorageCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Storage: Error handling is robust")]
    public async Task ErrorHandling_IsRobust()
    {
    // Arrange
    var storage = _fixture.GetStorageProvider();
    var badPath = "/does/not/exist.txt";

    // Act
    Func<Task> act = async () => await storage.DownloadAsync(badPath);

    // Assert
    await act.Should().ThrowAsync<Exception>().WithMessage("*not found*");
    }
}
