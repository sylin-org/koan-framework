using AwesomeAssertions;
using Koan.AI.Eval;
using Xunit;

namespace Koan.AI.Eval.Tests;

public class DriftResultTests
{
    [Fact]
    public void DriftStatus_OK_WhenScoreLow()
    {
        var result = new DriftResult
        {
            Score = 0.05,
            Status = DriftStatus.OK,
            TopShifts = ["accuracy"]
        };

        result.Status.Should().Be(DriftStatus.OK);
        result.Score.Should().BeLessThan(0.1);
        result.TopShifts.Should().Contain("accuracy");
        result.Recommendation.Should().BeNull();
    }

    [Fact]
    public void DriftStatus_EnumValues_Exist()
    {
        Enum.IsDefined(typeof(DriftStatus), DriftStatus.OK).Should().BeTrue();
        Enum.IsDefined(typeof(DriftStatus), DriftStatus.Notice).Should().BeTrue();
        Enum.IsDefined(typeof(DriftStatus), DriftStatus.Warning).Should().BeTrue();
    }
}
