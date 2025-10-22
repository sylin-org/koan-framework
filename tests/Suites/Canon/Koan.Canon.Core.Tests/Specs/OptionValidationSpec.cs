using Koan.TestPipeline;

namespace Koan.Canon.Core.Tests.Specs;

public class OptionValidationSpec : IClassFixture<CanonCoreTestPipelineFixture>
{
    private readonly CanonCoreTestPipelineFixture _fixture;

    public OptionValidationSpec(CanonCoreTestPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Canon: Option validation is correct")]
    public async Task OptionValidation_IsCorrect()
    {
    // Arrange
    var options = _fixture.GetOptions();
    options.SomeRequiredField = null;

    // Act
    var result = options.Validate();

    // Assert
    result.IsValid.Should().BeFalse();
    }
}
