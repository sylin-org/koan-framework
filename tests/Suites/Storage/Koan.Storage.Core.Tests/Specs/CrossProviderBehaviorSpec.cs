using Koan.TestPipeline;

namespace Koan.Storage.Core.Tests.Specs;

public class CrossProviderBehaviorSpec : IClassFixture<StorageCoreTestPipelineFixture>
{
    private readonly StorageCoreTestPipelineFixture _fixture;

    public CrossProviderBehaviorSpec(StorageCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Storage: Cross-provider behavior is correct")]
    public async Task CrossProviderBehavior_IsCorrect()
    {
    // Arrange
    var src = _fixture.GetStorageProvider("primary");
    var dst = _fixture.GetStorageProvider("secondary");
    var file = _fixture.CreateTestFile();

    // Act
    await src.Upload(file);
    await dst.CopyFrom(src, file.Path);
    var exists = await dst.Exists(file.Path);

    // Assert
    exists.Should().BeTrue();
    }
}
