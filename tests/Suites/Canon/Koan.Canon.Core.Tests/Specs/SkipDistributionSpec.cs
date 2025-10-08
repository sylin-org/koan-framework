using Koan.TestPipeline;

namespace Koan.Canon.Core.Tests.Specs;

public class SkipDistributionSpec : IClassFixture<CanonCoreTestPipelineFixture>
{
    private readonly CanonCoreTestPipelineFixture _fixture;

    public SkipDistributionSpec(CanonCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Canon: Skip distribution works as expected")]
    public async Task SkipDistribution_WorksAsExpected()
    {
    // Arrange
    var distributor = _fixture.GetDistributor();
    var entity = _fixture.CreateTestEntity();

    // Act
    await distributor.DistributeAsync(entity, skip: true);
    var distributed = distributor.GetDistributedEntities();

    // Assert
    distributed.Should().NotContain(e => e.Id == entity.Id);
    }
}
