using Koan.TestPipeline;

namespace Koan.Storage.Core.Tests.Specs;

public class MetadataPropagationSpec : IClassFixture<StorageCoreTestPipelineFixture>
{
    private readonly StorageCoreTestPipelineFixture _fixture;

    public MetadataPropagationSpec(StorageCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Storage: Metadata propagation is correct")]
    public async Task MetadataPropagation_IsCorrect()
    {
    // Arrange
    var storage = _fixture.GetStorageProvider();
    var file = _fixture.CreateTestFile();
    file.Metadata["x-test"] = "abc";

    // Act
    await storage.UploadAsync(file);
    var info = await storage.GetFileInfoAsync(file.Path);

    // Assert
    info.Metadata["x-test"].Should().Be("abc");
    }
}
