using Koan.TestPipeline;

namespace Koan.Canon.Core.Tests.Specs;

public class ObserverNotificationsSpec : IClassFixture<CanonCoreTestPipelineFixture>
{
    private readonly CanonCoreTestPipelineFixture _fixture;

    public ObserverNotificationsSpec(CanonCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Canon: Observer notifications are triggered correctly")]
    public async Task ObserverNotifications_AreTriggeredCorrectly()
    {
    // Arrange
    var observer = _fixture.GetObserver();
    var entity = _fixture.CreateTestEntity();

    // Act
    await observer.NotifyAsync(entity, "created");
    var notifications = observer.GetNotifications(entity.Id);

    // Assert
    notifications.Should().Contain(n => n.Event == "created");
    }
}
