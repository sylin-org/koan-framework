using Koan.Canon.Domain.Model;
using Xunit.Abstractions;

namespace Koan.Tests.Canon.Unit.Specs.Model;

public sealed class CanonStageSpec
{
    private readonly ITestOutputHelper _output;

    public CanonStageSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Attach_origin_and_canonical_id_updates_stage()
        => TestPipeline.For<CanonStageSpec>(_output, nameof(Attach_origin_and_canonical_id_updates_stage))
            .Assert(_ =>
            {
                var stage = new CanonStage<TestCanonModel>();
                var created = stage.CreatedAt;

                stage.AttachOrigin("crm");
                stage.AttachCanonicalId("canon-123");

                stage.Origin.Should().Be("crm");
                stage.CanonicalId.Should().Be("canon-123");
                stage.UpdatedAt.Should().BeAfter(created);
                stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Pending);
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Processing_and_completion_transitions_are_recorded()
        => TestPipeline.For<CanonStageSpec>(_output, nameof(Processing_and_completion_transitions_are_recorded))
            .Assert(_ =>
            {
                var stage = new CanonStage<TestCanonModel>();

                stage.MarkProcessing(actor: "system", notes: "batch");
                stage.MarkCompleted(actor: "system", notes: "done");

                stage.Status.Should().Be(CanonStageStatus.Completed);
                stage.ErrorCode.Should().BeNull();
                stage.ErrorMessage.Should().BeNull();
                stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Processing);
                stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Completed);
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Park_and_fail_track_errors()
        => TestPipeline.For<CanonStageSpec>(_output, nameof(Park_and_fail_track_errors))
            .Assert(_ =>
            {
                var stage = new CanonStage<TestCanonModel>();

                stage.Park("missing keys", actor: "validator");
                stage.Status.Should().Be(CanonStageStatus.Parked);
                stage.ErrorCode.Should().Be("parked");
                stage.ErrorMessage.Should().Be("missing keys");

                stage.MarkFailed("validation", "bad data", actor: "validator");
                stage.Status.Should().Be(CanonStageStatus.Failed);
                stage.ErrorCode.Should().Be("validation");
                stage.ErrorMessage.Should().Be("bad data");
                stage.Transitions.Last().Status.Should().Be(CanonStageStatus.Failed);
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Reset_to_pending_clears_error_state()
        => TestPipeline.For<CanonStageSpec>(_output, nameof(Reset_to_pending_clears_error_state))
            .Assert(_ =>
            {
                var stage = new CanonStage<TestCanonModel>();
                stage.MarkFailed("validation", "bad data");

                stage.ResetToPending();

                stage.Status.Should().Be(CanonStageStatus.Pending);
                stage.ErrorCode.Should().BeNull();
                stage.ErrorMessage.Should().BeNull();
                stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Pending && t != stage.Transitions.First());
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    private sealed class TestCanonModel : CanonEntity<TestCanonModel>
    {
    }
}
