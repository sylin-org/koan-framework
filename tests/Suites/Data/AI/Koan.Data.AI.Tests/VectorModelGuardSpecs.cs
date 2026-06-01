using FluentAssertions;
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
}
