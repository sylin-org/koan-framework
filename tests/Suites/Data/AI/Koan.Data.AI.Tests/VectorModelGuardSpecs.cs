using AwesomeAssertions;
using Koan.Data.AI;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// AI-0036 P2 (W4): the mixed-space / query-mismatch decision logic. Warn-only — these pin the
/// detection; the EmbeddingState-backed model-set read and WARN logging wrap this pure core.
/// </summary>
public sealed class VectorModelGuardSpecs
{
    [Fact]
    public void Single_model_index_is_clean()
    {
        var r = VectorModelGuard.Evaluate("Doc", new[] { "text-embedding-3-small" }, "text-embedding-3-small");
        r.MixedSpace.Should().BeFalse();
        r.QueryMismatch.Should().BeFalse();
    }

    [Fact]
    public void Two_models_is_mixed_space()
        => VectorModelGuard.Evaluate("Doc", new[] { "a", "b" }, null).MixedSpace.Should().BeTrue();

    [Fact]
    public void Query_model_not_in_index_is_a_mismatch()
        => VectorModelGuard.Evaluate("Doc", new[] { "a" }, "b").QueryMismatch.Should().BeTrue();

    [Fact]
    public void Query_model_in_index_is_not_a_mismatch()
        => VectorModelGuard.Evaluate("Doc", new[] { "a", "b" }, "a").QueryMismatch.Should().BeFalse();

    [Fact]
    public void Unknown_query_model_does_not_flag_mismatch()
        => VectorModelGuard.Evaluate("Doc", new[] { "a" }, null).QueryMismatch.Should().BeFalse();

    [Fact]
    public void Empty_index_does_not_flag_anything()
    {
        var r = VectorModelGuard.Evaluate("Doc", System.Array.Empty<string>(), "a");
        r.MixedSpace.Should().BeFalse();
        r.QueryMismatch.Should().BeFalse("an empty index cannot mismatch — there is nothing to compare against");
    }

    // --- W4 write-time decision (hard throw) ---

    [Fact]
    public void Write_to_empty_index_records()
        => VectorModelGuard.DecideWrite(System.Array.Empty<string>(), "a").Should().Be(ModelWriteAction.Record);

    [Fact]
    public void Write_same_model_is_noop()
        => VectorModelGuard.DecideWrite(new[] { "a" }, "a").Should().Be(ModelWriteAction.AlreadyPresent);

    [Fact]
    public void Write_second_model_into_single_model_index_throws()
        => VectorModelGuard.DecideWrite(new[] { "a" }, "b").Should().Be(ModelWriteAction.Throw);

    [Fact]
    public void Write_into_already_multi_model_index_warns_and_records()
        => VectorModelGuard.DecideWrite(new[] { "a", "b" }, "c").Should().Be(ModelWriteAction.WarnAndRecord);

    [Fact]
    public void Write_existing_model_into_multi_model_index_is_noop()
        => VectorModelGuard.DecideWrite(new[] { "a", "b" }, "a").Should().Be(ModelWriteAction.AlreadyPresent);
}
