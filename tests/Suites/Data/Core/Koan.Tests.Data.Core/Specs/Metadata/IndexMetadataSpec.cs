using System.Linq;
using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Metadata;

/// <summary>
/// DATA-0105 phase 0a: <see cref="IndexMetadata.GetIndexes"/> is Type-plane memoized and deterministic.
/// The prior implementation re-ran reflection per call (no cache) and used a per-call <c>Guid</c> as the
/// group key plus a <c>Dictionary</c> iteration — both non-deterministic. This pins the deterministic,
/// behaviour-preserving result and the memoization (the observable change).
/// </summary>
public class IndexMetadataSpec
{
    public class PkOnly : Entity<PkOnly>
    {
        public string Name { get; set; } = "";
    }

    public class SingleIndex : Entity<SingleIndex>
    {
        [Index] public string Email { get; set; } = "";
    }

    public class CompositeIndex : Entity<CompositeIndex>
    {
        [Index(Group = "g", Order = 2)] public string Last { get; set; } = "";
        [Index(Group = "g", Order = 1)] public string First { get; set; } = "";
    }

    public class TwoAnonymousOnSameProp : Entity<TwoAnonymousOnSameProp>
    {
        [Index]
        [Index(Unique = true)]
        public string Token { get; set; } = "";
    }

    [Fact]
    public void PkOnly_yields_a_single_primary_key_index()
    {
        var ix = IndexMetadata.GetIndexes(typeof(PkOnly));
        ix.Should().ContainSingle();
        ix[0].IsPrimaryKey.Should().BeTrue();
        ix[0].Unique.Should().BeTrue();
    }

    [Fact]
    public void Single_index_yields_pk_plus_one_nonpk_index()
    {
        var ix = IndexMetadata.GetIndexes(typeof(SingleIndex));
        ix.Where(i => i.IsPrimaryKey).Should().ContainSingle();
        var nonPk = ix.Where(i => !i.IsPrimaryKey).ToList();
        nonPk.Should().ContainSingle();
        nonPk[0].Properties.Select(p => p.Name).Should().Equal("Email");
    }

    [Fact]
    public void Composite_group_orders_properties_by_Order()
    {
        var ix = IndexMetadata.GetIndexes(typeof(CompositeIndex));
        var composite = ix.Single(i => !i.IsPrimaryKey);
        composite.Properties.Select(p => p.Name).Should().Equal("First", "Last");
    }

    [Fact]
    public void Two_anonymous_indexes_on_same_property_stay_two_distinct_indexes()
    {
        // The deterministic group key must NOT merge two AllowMultiple [Index] on one property
        // (the prior Guid disambiguator did this; the replacement uses the attribute position).
        var ix = IndexMetadata.GetIndexes(typeof(TwoAnonymousOnSameProp));
        ix.Where(i => !i.IsPrimaryKey).Should().HaveCount(2);
    }

    [Fact]
    public void GetIndexes_is_memoized_per_type()
    {
        var a = IndexMetadata.GetIndexes(typeof(CompositeIndex));
        var b = IndexMetadata.GetIndexes(typeof(CompositeIndex));
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void GetIndexes_result_is_stable()
    {
        var first = IndexMetadata.GetIndexes(typeof(CompositeIndex)).Select(Describe).ToList();
        for (var i = 0; i < 50; i++)
        {
            IndexMetadata.GetIndexes(typeof(CompositeIndex)).Select(Describe).Should().Equal(first);
        }

        static string Describe(IndexSpec s) =>
            $"{s.IsPrimaryKey}:{s.Unique}:{s.Ttl}:{string.Join(',', s.Properties.Select(p => p.Name))}";
    }
}
