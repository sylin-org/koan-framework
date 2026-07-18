using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core.Sorting;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

public sealed class SqliteProviderBoundedPagingSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: raw predicates stay unbounded unless shaping requests a page")]
    public async Task Raw_predicates_do_not_invent_a_default_page()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("raw-pagination-intent"));

        await PagingProbe.UpsertMany(Enumerable.Range(1, 75)
            .Select(rank => new PagingProbe { Rank = rank }));

        var all = await Data<PagingProbe, string>.QueryRaw("1 = 1");
        var page = await Data<PagingProbe, string>.QueryRaw(
            "1 = 1",
            shaping: QueryDefinition.All.WithPagination(page: 2, pageSize: 7));

        all.Should().HaveCount(75);
        page.Should().HaveCount(7);
    }

    [Fact(DisplayName = "Sqlite: provider paging is bounded, count-free by default, and gated by total sort handling")]
    public async Task Provider_paging_reports_only_the_work_it_performed()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("provider-bounded-paging"));

        await PagingProbe.UpsertMany([
            new PagingProbe { Rank = 3, Detail = new ProbeDetail { Rank = 30 } },
            new PagingProbe { Rank = 1, Detail = new ProbeDetail { Rank = 10 } },
            new PagingProbe { Rank = 2, Detail = new ProbeDetail { Rank = 20 } },
        ]);

        var repository = host.Services.GetRequiredService<IDataService>()
            .GetRepository<PagingProbe, string>();
        var queryRepository = (IQueryRepository<PagingProbe, string>)repository;

        DataCaps.Describe(repository, repository.GetType().Name)
            .Has(DataCaps.Query.ProviderBoundedPaging)
            .Should().BeTrue();

        var boundedQuery = QueryDefinition.All
            .WithSort<PagingProbe>(sort => sort.OrderBy(probe => probe.Rank).ThenBy(probe => probe.Id))
            .WithPagination(page: 1, pageSize: 2);

        var bounded = await queryRepository.Query(boundedQuery);
        bounded.PaginationHandled.Should().BeTrue();
        bounded.SortFullyHandled(boundedQuery).Should().BeTrue();
        bounded.Items.Select(static probe => probe.Rank).Should().Equal(1, 2);
        bounded.TotalCount.Should().BeNull("numbered paging alone does not request a total");

        var counted = await queryRepository.Query(boundedQuery.WithCountStrategy(CountStrategy.Exact));
        counted.TotalCount.Should().Be(3);

        var nestedSortQuery = QueryDefinition.All
            .WithSort<PagingProbe>(sort => sort.OrderBy(probe => probe.Detail.Rank))
            .WithPagination(page: 1, pageSize: 2);

        var nestedSort = await queryRepository.Query(nestedSortQuery);
        nestedSort.PaginationHandled.Should().BeFalse("SQLite cannot bound a page before completing a nested sort");
        nestedSort.SortFullyHandled(nestedSortQuery).Should().BeFalse();
        nestedSort.Items.Should().HaveCount(3, "the coordinator needs the complete candidate set for its sort fallback");
        nestedSort.TotalCount.Should().BeNull();
    }

    private sealed class PagingProbe : Entity<PagingProbe>
    {
        public int Rank { get; set; }
        public ProbeDetail Detail { get; set; } = new();
    }

    private sealed class ProbeDetail
    {
        public int Rank { get; set; }
    }
}
