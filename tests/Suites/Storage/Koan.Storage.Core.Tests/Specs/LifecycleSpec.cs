using Koan.TestPipeline;

namespace Koan.Storage.Core.Tests.Specs;

public class LifecycleSpec : IClassFixture<StorageCoreTestPipelineFixture>
{
    private readonly StorageCoreTestPipelineFixture _fixture;

    public LifecycleSpec(StorageCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Storage: Lifecycle operations work as expected")]
    public async Task Lifecycle_OperationsWorkAsExpected()
    {
    // Arrange
    var storage = _fixture.GetStorageProvider();
    var file = _fixture.CreateTestFile();

    // Act
    await storage.UploadAsync(file);
    var exists = await storage.ExistsAsync(file.Path);
    await storage.DeleteAsync(file.Path);
    var existsAfterDelete = await storage.ExistsAsync(file.Path);

    // Assert
    exists.Should().BeTrue();
    existsAfterDelete.Should().BeFalse();
    }
}
