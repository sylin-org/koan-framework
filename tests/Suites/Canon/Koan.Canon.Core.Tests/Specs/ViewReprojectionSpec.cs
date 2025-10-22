using Koan.TestPipeline;

namespace Koan.Canon.Core.Tests.Specs;

public class ViewReprojectionSpec : IClassFixture<CanonCoreTestPipelineFixture>
{
    private readonly CanonCoreTestPipelineFixture _fixture;

    public ViewReprojectionSpec(CanonCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Canon: View reprojection is correct")]
    public async Task ViewReprojection_IsCorrect()
    {
    // Arrange
    var view = _fixture.GetView();
    var data = _fixture.CreateTestData();

    // Act
    var reprojected = view.Reproject(data);

    // Assert
    reprojected.Should().NotBeNull();
    reprojected!.IsValid.Should().BeTrue();
    }
}
