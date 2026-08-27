using Koan.Canon;
using Koan.Data.Core.Polymorphism;

namespace Koan.Tests.Canon.Unit.Specs.Model;

public sealed class CanonStageSpec
{
    [Fact]
    public void Attach_origin_and_canonical_id_updates_stage()
    {
        var stage = new CanonStage<TestCanonModel>();
        var created = stage.CreatedAt;

        stage.AttachOrigin("crm");
        stage.AttachCanonicalId("canon-123");

        stage.Origin.Should().Be("crm");
        stage.CanonicalId.Should().Be("canon-123");
        stage.UpdatedAt.Should().BeAfter(created);
        stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Pending);
    }

    [Fact]
    public void Processing_and_completion_transitions_are_recorded()
    {
        var stage = new CanonStage<TestCanonModel>();

        stage.MarkProcessing(actor: "system", notes: "batch");
        stage.MarkCompleted(actor: "system", notes: "done");

        stage.Status.Should().Be(CanonStageStatus.Completed);
        stage.ErrorCode.Should().BeNull();
        stage.ErrorMessage.Should().BeNull();
        stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Processing);
        stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Completed);
    }

    [Fact]
    public void Park_and_fail_track_errors()
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
    }

    [Fact]
    public void Reset_to_pending_clears_error_state()
    {
        var stage = new CanonStage<TestCanonModel>();
        stage.MarkFailed("validation", "bad data");

        stage.ResetToPending();

        stage.Status.Should().Be(CanonStageStatus.Pending);
        stage.ErrorCode.Should().BeNull();
        stage.ErrorMessage.Should().BeNull();
        stage.Transitions.Should().Contain(t => t.Status == CanonStageStatus.Pending && t != stage.Transitions.First());
    }

    /// <summary>
    /// PMC-061: the constructor owns the "Stage created" seed, and hydration is store-authoritative — the
    /// stored document is the complete history, so re-materializing must REPLACE the in-memory list, never
    /// add stored transitions on top of the constructor's seed. Before the fix, Newtonsoft's default
    /// Auto handling duplicated the seed on every save/reload cycle (3–5 per receipt in the blind run).
    /// </summary>
    [Fact]
    public void Reload_keeps_exactly_one_stage_created_transition()
    {
        var stage = new CanonStage<TestCanonModel>();
        stage.MarkProcessing(actor: "engine");
        stage.MarkCompleted(actor: "engine", notes: "done");

        // Persist + reload twice through the shared document materializer — the eager-store path. Each
        // cycle re-materializes what the previous cycle wrote, the growth-per-cycle shape of the field incident.
        for (var cycle = 1; cycle <= 2; cycle++)
        {
            var json = EntityJsonSerialization.SerializeDocument(stage);
            stage = (CanonStage<TestCanonModel>)EntityJsonSerialization.DeserializeDocument(
                json, typeof(CanonStage<TestCanonModel>));

            stage.Transitions.Count(t => t.Notes == "Stage created").Should().Be(1,
                $"reload cycle {cycle} must restore stored history, not append it to a fresh constructor seed");
        }
        stage.Transitions.Select(t => t.Status).Should().Equal(
            CanonStageStatus.Pending, CanonStageStatus.Processing, CanonStageStatus.Completed);
    }

    private sealed class TestCanonModel : CanonEntity<TestCanonModel>
    {
    }
}
