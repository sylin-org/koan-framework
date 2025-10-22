using Koan.TestPipeline;

namespace Koan.Canon.Core.Tests.Specs;

public class AppHostShortcutsSpec : IClassFixture<CanonCoreTestPipelineFixture>
{
    private readonly CanonCoreTestPipelineFixture _fixture;

    public AppHostShortcutsSpec(CanonCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Canon: AppHost shortcuts work as expected")]
    public async Task AppHostShortcuts_WorkAsExpected()
    {
    // Arrange
    var appHost = _fixture.GetAppHost();

    // Act
    var shortcut = appHost.GetShortcut("test");

    // Assert
    shortcut.Should().NotBeNull();
    shortcut!.Name.Should().Be("test");
    }
}
