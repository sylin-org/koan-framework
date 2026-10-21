using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;
using Xunit;

namespace Koan.Data.Relational.Tests;

/// <summary>
/// The oracle (<see cref="InMemoryFilterEvaluator"/>) is two-valued: a predicate over a null or missing field is
/// false, and its negation is true. SQL is three-valued, so every negating form the translator emits must say what
/// happens to NULL explicitly (NK-020, NK-021, NK-051). Behaviour is proven against real stores by the shared
/// convergence corpus; these pin the emitted shape every relational dialect receives.
/// </summary>
public sealed class SqlFilterNullSemanticsSpec
{
    private static (string Sql, IReadOnlyList<object?> Parameters) Translate(Filter filter)
    {
        var plan = RelationalManagedMapping.Compile<Scored>("Default", StorageAddress.From("SCORED"));
        return new SqlFilterTranslator(new ReadDialect(), plan).Translate(filter);
    }

    private static Filter On(FilterOperator op, FilterValue value) => Filter.On(FieldPath.Of("Score"), op, value);

    [Fact]
    public void Ne_keeps_null_rows()
    {
        var (sql, parameters) = Translate(On(FilterOperator.Ne, FilterValue.Of(100)));
        sql.Should().Be("(read(Score) IS NULL OR read(Score) <> @p0)");
        parameters.Should().Equal(100);
    }

    [Fact]
    public void Ne_null_is_not_null()
        => Translate(On(FilterOperator.Ne, FilterValue.Of(null))).Sql.Should().Be("(read(Score) IS NOT NULL)");

    [Fact]
    public void Not_folds_unknown_to_false_before_negating()
        => Translate(new Not(On(FilterOperator.Gt, FilterValue.Of(80)))).Sql
            .Should().Be("(CASE WHEN (read(Score) > @p0) THEN 1 ELSE 0 END = 0)");

    [Fact]
    public void In_with_null_member_matches_null()
    {
        var (sql, parameters) = Translate(Filter.In("Score", new object?[] { null, 100 }));
        sql.Should().Be("(read(Score) IS NULL OR read(Score) IN (@p0))");
        parameters.Should().Equal(100);
    }

    [Fact]
    public void In_with_only_null_is_null()
        => Translate(Filter.In("Score", new object?[] { null })).Sql.Should().Be("(read(Score) IS NULL)");

    [Fact]
    public void Nin_without_null_member_matches_null()
        => Translate(On(FilterOperator.Nin, FilterValue.Many(new object?[] { 100, 300 }))).Sql
            .Should().Be("(read(Score) IS NULL OR read(Score) NOT IN (@p0, @p1))");

    [Fact]
    public void Nin_with_null_member_excludes_null()
    {
        var (sql, parameters) = Translate(On(FilterOperator.Nin, FilterValue.Many(new object?[] { null, 100 })));
        sql.Should().Be("(read(Score) IS NOT NULL AND read(Score) NOT IN (@p0))");
        parameters.Should().Equal(100);
    }

    [Fact]
    public void Nin_with_only_null_is_not_null()
        => Translate(On(FilterOperator.Nin, FilterValue.Many(new object?[] { null }))).Sql.Should().Be("(read(Score) IS NOT NULL)");

    public sealed class Scored
    {
        public string Id { get; set; } = "";
        public int? Score { get; set; }
    }

    private sealed class ReadDialect : IRelationalMappingDialect
    {
        public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType) => $"read({path.Segments.Last()})";
        public string QuoteIdent(string ident) => $"\"{ident}\"";
        public string EscapeLike(string fragment) => fragment;
        public string Parameter(int index) => $"@p{index}";
        public string JsonArrayContains(string columnSql, string parameter) => $"contains({columnSql},{parameter})";
        public string JsonArrayLength(string columnSql) => $"length({columnSql})";
        public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
            $"elementlike({columnSql},{patternParameter},{literalParameter})";
    }
}
