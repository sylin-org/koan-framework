using FluentAssertions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector;
using Koan.Data.Vector.Querying;
using Xunit;

namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// AI-0036 §9 / DATA-0097 P1: the residual-is-error gate. The coordinator returns a fully-pushable
/// filter or throws <see cref="VectorFilterUnsupportedException"/> — it NEVER returns a residual for
/// in-memory evaluation (vector search has no in-memory floor; post-kNN filtering under-returns).
/// </summary>
public sealed class VectorFilterCoordinatorSpecs
{
    private static readonly VectorFilterCapabilities ScalarOnly =
        VectorFilterCapabilities.Of(nestedPaths: true, ignoreCase: false,
            FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Gt, FilterOperator.Gte,
            FilterOperator.Lt, FilterOperator.Lte, FilterOperator.In, FilterOperator.Nin);

    [Fact]
    public void Null_filter_returns_null()
        => VectorFilterCoordinator.Validate(null, ScalarOnly, "test").Should().BeNull();

    [Fact]
    public void Fully_pushable_filter_returns_the_tree()
    {
        var f = Filter.Eq("tenant", "acme");
        VectorFilterCoordinator.Validate(f, ScalarOnly, "test").Should().BeSameAs(f);
    }

    [Fact]
    public void Unsupported_operator_throws_naming_operator_and_field()
    {
        var f = Filter.On(FieldPath.Of("name"), FilterOperator.Contains, FilterValue.Of("x"));
        var ex = ((Action)(() => VectorFilterCoordinator.Validate(f, ScalarOnly, "pgvector")))
            .Should().Throw<VectorFilterUnsupportedException>().Which;
        ex.Operator.Should().Be(FilterOperator.Contains);
        ex.Field.Should().Be("name");
        ex.Provider.Should().Be("pgvector");
    }

    [Fact]
    public void Partial_conjunction_with_one_unsupported_leaf_throws()
    {
        // Eq is pushable, Contains is not -> residual -> hard error (never silent in-memory).
        var f = Filter.All(
            Filter.Eq("tenant", "acme"),
            Filter.On(FieldPath.Of("name"), FilterOperator.Contains, FilterValue.Of("x")));
        ((Action)(() => VectorFilterCoordinator.Validate(f, ScalarOnly, "test")))
            .Should().Throw<VectorFilterUnsupportedException>();
    }

    [Fact]
    public void Disjunction_with_one_unsupported_leaf_throws_wholesale()
    {
        var f = Filter.Any(
            Filter.Eq("tenant", "acme"),
            Filter.On(FieldPath.Of("name"), FilterOperator.Contains, FilterValue.Of("x")));
        ((Action)(() => VectorFilterCoordinator.Validate(f, ScalarOnly, "test")))
            .Should().Throw<VectorFilterUnsupportedException>();
    }

    [Fact]
    public void ClrFilter_is_a_hard_error()
    {
        System.Linq.Expressions.Expression<System.Func<int, bool>> e = i => i > 0;
        ((Action)(() => VectorFilterCoordinator.Validate(new ClrFilter(e), ScalarOnly, "test")))
            .Should().Throw<VectorFilterUnsupportedException>();
    }

    [Fact]
    public void IgnoreCase_node_throws_when_capability_lacks_it()
    {
        var f = ((FieldFilter)Filter.On(FieldPath.Of("name"), FilterOperator.Eq, FilterValue.Of("acme")))
            with { IgnoreCase = true };
        ((Action)(() => VectorFilterCoordinator.Validate(f, ScalarOnly, "test")))
            .Should().Throw<VectorFilterUnsupportedException>("ScalarOnly has IgnoreCase=false");
    }

    [Fact]
    public void IgnoreCase_node_passes_when_capability_allows_it()
    {
        var caps = VectorFilterCapabilities.Of(nestedPaths: true, ignoreCase: true, FilterOperator.Eq);
        var f = (FieldFilter)Filter.On(FieldPath.Of("name"), FilterOperator.Eq, FilterValue.Of("acme")) with { IgnoreCase = true };
        VectorFilterCoordinator.Validate(f, caps, "test").Should().BeSameAs(f);
    }
}
