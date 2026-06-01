using FluentAssertions;
using Koan.Data.AI;
using Koan.Data.Vector;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// AI-0036 W1/W2/W3: the embedding lifecycle owner must stamp the producing model/source onto the
/// stored vector instead of dropping it (every <c>SaveWithVector</c> site previously passed null).
/// These prove <see cref="VectorProvenance.Build"/> — the single choke point all three write paths
/// route through — produces the reserved <c>__embedding.*</c> metadata, and preserves the prior
/// null contract only when there is genuinely nothing to record.
/// </summary>
public sealed class VectorProvenanceSpecs
{
    [Fact]
    public void Model_source_version_become_reserved_keys()
    {
        var meta = VectorProvenance.Build("text-embedding-3-large", "openai-prod", version: 3);

        meta.Should().NotBeNull();
        meta![VectorProvenanceKeys.Model].Should().Be("text-embedding-3-large");
        meta[VectorProvenanceKeys.Source].Should().Be("openai-prod");
        meta[VectorProvenanceKeys.Version].Should().Be(3);
    }

    [Fact]
    public void Provider_is_derived_from_source_when_not_supplied()
        => VectorProvenance.Build("m", "openai-prod", version: 0)![VectorProvenanceKeys.Provider]
            .Should().Be("openai");

    [Fact]
    public void Explicit_provider_wins_over_derivation()
        => VectorProvenance.Build("m", "openai-prod", version: 0, provider: "azure")![VectorProvenanceKeys.Provider]
            .Should().Be("azure");

    [Fact]
    public void Version_zero_is_treated_as_unset_and_omitted()
        => VectorProvenance.Build("m", source: null, version: 0)
            .Should().NotContainKey(VectorProvenanceKeys.Version);

    [Fact]
    public void Nothing_to_record_and_nothing_to_merge_preserves_the_null_contract()
        => VectorProvenance.Build(model: null, source: null, version: 0).Should().BeNull();

    [Fact]
    public void Caller_metadata_is_carried_through_alongside_provenance()
    {
        var caller = new Dictionary<string, object> { ["tenant"] = "acme" };
        var meta = VectorProvenance.Build("m", "openai", version: 1, merge: caller);

        meta.Should().NotBeNull();
        meta!["tenant"].Should().Be("acme");
        meta[VectorProvenanceKeys.Model].Should().Be("m");
    }

    [Fact]
    public void Reserved_keys_are_authoritative_over_a_colliding_caller_key()
    {
        var caller = new Dictionary<string, object> { [VectorProvenanceKeys.Model] = "stale-model" };
        var meta = VectorProvenance.Build("real-model", source: null, version: 0, merge: caller);

        meta![VectorProvenanceKeys.Model].Should().Be("real-model");
    }

    [Fact]
    public void Pure_caller_metadata_with_no_provenance_is_still_returned()
    {
        var caller = new Dictionary<string, object> { ["tenant"] = "acme" };
        var meta = VectorProvenance.Build(model: null, source: null, version: 0, merge: caller);

        meta.Should().NotBeNull();
        meta!["tenant"].Should().Be("acme");
    }

    [Fact]
    public void Derive_provider_handles_null_and_unprefixed_sources()
    {
        VectorProvenance.DeriveProvider(null).Should().BeNull();
        VectorProvenance.DeriveProvider("openai").Should().Be("openai");
        VectorProvenance.DeriveProvider("openai-prod-east").Should().Be("openai");
    }
}
