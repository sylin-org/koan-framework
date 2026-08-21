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

    [Fact(DisplayName = "an order the caller named is never displaced")]
    public void Caller_order_wins()
    {
        var query = QueryDefinition.All
            .WithPagination(1, 10)
            .WithSort(Koan.Data.Core.Sorting.SortSpecParser.ParseStrict<Widget>("-Name"));

        var (adapterQuery, _) = FilterPushdownCoordinator.Plan(query, FilterSupport.None, typeof(Widget));

        adapterQuery.Sort.Should().ContainSingle()
            .Which.Path.Members.Single().Name.Should().Be(nameof(Widget.Name));
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
