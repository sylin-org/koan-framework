using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Model;
using Koan.Data.Core.Querying;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Querying;

/// <summary>
/// No adapter is ever asked to take a page from an unordered set (DATA-0119).
///
/// <para>A page is a window onto an order, so paging without one is meaningless rather than merely weaker: the
/// store may return different rows for page two than page one implied. Each adapter used to answer this
/// privately and one answered <c>ORDER BY (SELECT NULL)</c> — enough to satisfy SQL Server's requirement that
/// OFFSET have an ORDER BY, and no ordering whatever.</para>
///
/// <para>This asserts the decision, not a sample of its effects. An end-to-end check cannot prove it: five rows
/// come back from a small table in physical order whether or not anyone asked for one, so such a test passes
/// with the guarantee removed. What can be proven is the property the guarantee actually is — that the
/// definition handed to the adapter carries an order.</para>
/// </summary>
public sealed class PageOrderOwnershipSpec
{
    private sealed class Widget : Entity<Widget>
    {
        public string Name { get; set; } = "";
    }

    [Fact(DisplayName = "a paged query reaches the adapter with an order, even when the caller named none")]
    public void Paged_query_carries_an_order()
    {
        var query = QueryDefinition.All.WithPagination(2, 10);
        query.HasSort.Should().BeFalse("the caller asked for no particular order");

        var (adapterQuery, _) = FilterPushdownCoordinator.Plan(query, FilterSupport.None, typeof(Widget));

        adapterQuery.Sort.Should().ContainSingle("the framework supplies the order a page is a window onto")
            .Which.Path.Members.Single().Name.Should().Be(nameof(Entity<Widget>.Id));
        adapterQuery.Sort[0].Desc.Should().BeFalse();
    }

    [Fact(DisplayName = "an order the caller named leads, and the identity settles the ties it leaves")]
    public void Caller_order_leads_and_identity_breaks_ties()
    {
        // Naming a sort is not naming a total order. Ordering by Name, where rows share a Name, leaves the
        // store free to break those ties differently on each request - so page two repeats and skips exactly
        // as it would with no sort at all.
        var query = QueryDefinition.All
            .WithPagination(1, 10)
            .WithSort(Koan.Data.Core.Sorting.SortSpecParser.ParseStrict<Widget>("-Name"));

        var (adapterQuery, _) = FilterPushdownCoordinator.Plan(query, FilterSupport.None, typeof(Widget));

        adapterQuery.Sort.Should().HaveCount(2);
        adapterQuery.Sort[0].Path.Members.Single().Name.Should().Be(nameof(Widget.Name), "the caller leads");
        adapterQuery.Sort[0].Desc.Should().BeTrue("the caller's direction is untouched");
        adapterQuery.Sort[1].Path.Members.Single().Name.Should().Be(nameof(Entity<Widget>.Id));
    }

    [Fact(DisplayName = "an unpaged read keeps exactly the order the caller named")]
    public void Unpaged_caller_order_is_untouched()
    {
        var query = QueryDefinition.All.WithSort(
            Koan.Data.Core.Sorting.SortSpecParser.ParseStrict<Widget>("-Name"));

        var (adapterQuery, _) = FilterPushdownCoordinator.Plan(query, FilterSupport.None, typeof(Widget));

        adapterQuery.Sort.Should().ContainSingle(
            "with no window to be a window of, ties cost nothing and a second key would cost a sort");
    }

    [Fact(DisplayName = "a caller already ordering by identity gets no redundant second key")]
    public void Identity_is_not_appended_twice()
    {
        var query = QueryDefinition.All
            .WithPagination(1, 10)
            .WithSort(Koan.Data.Core.Sorting.SortSpecParser.ParseStrict<Widget>("Id"));

        var (adapterQuery, _) = FilterPushdownCoordinator.Plan(query, FilterSupport.None, typeof(Widget));

        adapterQuery.Sort.Should().ContainSingle("a key cannot break its own ties");
    }

    [Fact(DisplayName = "an unpaged read keeps whatever order the store finds cheapest")]
    public void Unpaged_read_is_untouched()
    {
        var query = QueryDefinition.All;

        var (adapterQuery, _) = FilterPushdownCoordinator.Plan(query, FilterSupport.None, typeof(Widget));

        adapterQuery.Sort.Should().BeEmpty(
            "there is no window for an order to be the window of, so the read pays nothing for one");
    }
}
