using System.Collections.Frozen;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Model;
using Koan.Data.Core.Querying;
using Koan.Data.Core.Sorting;

namespace Koan.Tests.Data.Core.Specs.Querying;

public sealed class QueryCoordinationReceiptSpec
{
    [Fact]
    public void False_filter_receipt_rejects_instead_of_returning_unproved_rows()
    {
        var query = QueryDefinition.All.Where(Filter.Eq(nameof(ProjectedEntity.Name), "kept"));
        var (adapterQuery, residual) = FilterPushdownCoordinator.Plan(
            query,
            FilterSupport.Full,
            typeof(ProjectedEntity));
        var result = new RepositoryQueryResult<ProjectedEntity>
        {
            Items = [new ProjectedEntity { Id = "one", Name = "wrong" }],
            FilterHandled = false
        };

        var finalize = () => FilterPushdownCoordinator.Finalize(query, adapterQuery, residual, result);

        finalize.Should().Throw<QueryReceiptRejectedException>()
            .Which.Axis.Should().Be(QueryReceiptAxis.Filter);
    }

    [Fact]
    public void Residual_plan_rejects_impossible_provider_pagination_receipt()
    {
        var filter = Filter.On(
            FieldPath.Of(nameof(ProjectedEntity.Name)),
            FilterOperator.Eq,
            FilterValue.Of("kept"));
        var query = QueryDefinition.All.Where(filter).WithPagination(1, 1);
        var (adapterQuery, residual) = FilterPushdownCoordinator.Plan(
            query,
            FilterSupport.None,
            typeof(ProjectedEntity));
        adapterQuery.HasPagination.Should().BeFalse();
        var result = new RepositoryQueryResult<ProjectedEntity>
        {
            Items = [new ProjectedEntity { Id = "one", Name = "kept" }],
            PaginationHandled = true
        };

        var finalize = () => FilterPushdownCoordinator.Finalize(query, adapterQuery, residual, result);

        finalize.Should().Throw<QueryReceiptRejectedException>()
            .Which.Axis.Should().Be(QueryReceiptAxis.Pagination);
    }

    [Fact]
    public void Provider_paged_count_requires_an_unpaginated_total_receipt()
    {
        var query = QueryDefinition.All.WithPagination(1, 1).WithCountStrategy(CountStrategy.Exact);
        var result = new RepositoryQueryResult<ProjectedEntity>
        {
            Items = [new ProjectedEntity { Id = "one" }],
            PaginationHandled = true
        };

        var finalize = () => FilterPushdownCoordinator.Finalize(query, query, residual: null, result);

        finalize.Should().Throw<QueryReceiptRejectedException>()
            .Which.Axis.Should().Be(QueryReceiptAxis.Count);
    }

    [Fact]
    public void Sort_receipt_may_only_name_requested_components()
    {
        var requested = QueryDefinition.All.WithSort<ProjectedEntity>(sort => sort.OrderBy(entity => entity.Name));
        var unrelated = QueryDefinition.All.WithSort<ProjectedEntity>(sort => sort.OrderBy(entity => entity.Secret));
        var result = new RepositoryQueryResult<ProjectedEntity>
        {
            Items = [],
            SortHandled = unrelated.Sort.ToFrozenSet()
        };

        var finalize = () => FilterPushdownCoordinator.Finalize(requested, requested, residual: null, result);

        finalize.Should().Throw<QueryReceiptRejectedException>()
            .Which.Axis.Should().Be(QueryReceiptAxis.Sort);
    }

    private sealed class ProjectedEntity : Entity<ProjectedEntity, string>
    {
        public override string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
    }
}
