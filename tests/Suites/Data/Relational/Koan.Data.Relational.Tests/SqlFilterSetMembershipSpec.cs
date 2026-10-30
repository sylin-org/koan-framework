using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;
using Xunit;

namespace Koan.Data.Relational.Tests;

/// <summary>
/// DATA-0162: a set binds as one value where the dialect carries one, else one parameter per member in IN lists the
/// dialect allows; and a <c>Not</c> over a field's <c>In</c> lowers as its <c>Nin</c>, the exact complement under the
/// null rules <see cref="SqlFilterNullSemanticsSpec"/> pins.
/// </summary>
public sealed class SqlFilterSetMembershipSpec
{
    private static (string Sql, IReadOnlyList<object?> Parameters) Translate(Filter filter, IRelationalMappingDialect dialect)
    {
        var plan = RelationalManagedMapping.Compile<Scored>("Default", StorageAddress.From("SCORED"));
        return new SqlFilterTranslator(dialect, plan).Translate(filter);
    }

    private static Filter In(params object?[] members) => Filter.On(FieldPath.Of("Score"), FilterOperator.In, FilterValue.Many(members));

    [Fact]
    public void A_dialect_that_carries_the_set_binds_it_as_one_value()
    {
        var (sql, parameters) = Translate(In(1, 2, 3), new SetDialect());
        sql.Should().Be("(read(Score) = ANY(@p0))");
        parameters.Should().ContainSingle().Which.Should().BeEquivalentTo(new object[] { 1, 2, 3 });
    }

    [Fact]
    public void A_null_member_stays_outside_the_bound_set()
    {
        var (sql, parameters) = Translate(In(null, 1), new SetDialect());
        sql.Should().Be("(read(Score) IS NULL OR read(Score) = ANY(@p0))");
        parameters.Should().ContainSingle();
    }

    [Fact]
    public void Members_past_the_in_list_bound_are_split_into_several_lists()
    {
        Translate(In(1, 2, 3, 4, 5), new ListDialect()).Sql
            .Should().Be("((read(Score) IN (@p0, @p1) OR read(Score) IN (@p2, @p3) OR read(Score) IN (@p4)))");
    }

    [Fact]
    public void A_split_nin_joins_its_lists_with_and()
        => Translate(Filter.On(FieldPath.Of("Score"), FilterOperator.Nin, FilterValue.Many(new object?[] { 1, 2, 3 })), new ListDialect()).Sql
            .Should().Be("(read(Score) IS NULL OR (read(Score) NOT IN (@p0, @p1) AND read(Score) NOT IN (@p2)))");

    [Fact]
    public void A_statement_past_the_parameters_with_no_set_that_can_move_is_refused_by_name()
    {
        var translate = () => Translate(In(1, 2, 3, 4), new BoundedDialect());
        translate.Should().Throw<NotSupportedException>()
            .WithMessage("*binds 4 parameters, more than the 3*4 Int32*");
        Translate(In(1, 2, 3), new BoundedDialect()).Parameters.Should().HaveCount(3);
    }

    [Fact]
    public void A_collection_list_binds_as_one_value_wherever_the_dialect_carries_it()
    {
        // Even under a dialect that prefers parameter lists, and within the bound: the per-member form reads the array
        // once per member.
        var (sql, parameters) = Translate(
            Filter.HasAny("Tags", new object?[] { "a", "b", "c" }), new ParameterPreferringDialect());
        sql.Should().Be("any(read(Tags), @p0)");
        parameters.Should().ContainSingle();
    }

    [Fact]
    public void A_statement_of_plain_comparisons_past_the_bound_says_so()
    {
        var filter = Filter.All(
            Filter.Eq("Score", 1), Filter.Eq("Level", 2), Filter.Eq("Score", 3), Filter.Eq("Level", 4));
        var translate = () => Translate(filter, new BoundedDialect());
        translate.Should().Throw<NotSupportedException>().WithMessage("*binds 4 parameters*plain comparisons*");
    }

    [Fact]
    public void The_bound_belongs_to_the_statement_and_the_largest_set_moves_first()
    {
        // Two sets of 3 and 4 members are 7 parameters, past the 5 the statement allows: the set of 4 moves to one value.
        var filter = Filter.All(
            In(1, 2, 3),
            Filter.On(FieldPath.Of("Level"), FilterOperator.In, FilterValue.Many(new object?[] { 1, 2, 3, 4 })));
        var (sql, parameters) = Translate(filter, new ParameterPreferringDialect());
        parameters.Should().HaveCount(4);
        sql.Should().Contain("read(Score) IN (@p0, @p1, @p2)").And.Contain("read(Level) = ANY(@p3)");

        // Within the bound both stay parameter lists.
        Translate(Filter.All(In(1, 2), In(3, 4)), new ParameterPreferringDialect()).Sql.Should().NotContain("ANY");
    }

    [Fact]
    public void Not_over_in_lowers_as_nin()
        => Translate(new Not(In(1, 2)), new ListDialect()).Sql
            .Should().Be(Translate(Filter.On(FieldPath.Of("Score"), FilterOperator.Nin, FilterValue.Many(new object?[] { 1, 2 })), new ListDialect()).Sql);

    [Fact]
    public void Not_over_nin_lowers_as_in()
        => Translate(new Not(Filter.On(FieldPath.Of("Score"), FilterOperator.Nin, FilterValue.Many(new object?[] { null, 1 }))), new ListDialect()).Sql
            .Should().Be("(read(Score) IS NULL OR read(Score) IN (@p0))");

    public sealed class Scored
    {
        public string Id { get; set; } = "";
        public int? Score { get; set; }
        public int Level { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private class ListDialect : IRelationalMappingDialect
    {
        public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType) => $"read({path.Segments.Last()})";
        public string QuoteIdent(string ident) => $"\"{ident}\"";
        public string EscapeLike(string fragment) => fragment;
        public string Parameter(int index) => $"@p{index}";
        public string JsonArrayContains(string columnSql, string parameter) => $"contains({columnSql},{parameter})";
        public string JsonArrayLength(string columnSql) => $"length({columnSql})";
        public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
            $"elementlike({columnSql},{patternParameter},{literalParameter})";
        public int MaximumInListMembers => 2;
    }

    private sealed class BoundedDialect : IRelationalMappingDialect
    {
        private readonly ListDialect _list = new();
        public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType) => _list.Read(path, shape, physicalType);
        public string QuoteIdent(string ident) => _list.QuoteIdent(ident);
        public string EscapeLike(string fragment) => fragment;
        public string Parameter(int index) => $"@p{index}";
        public string JsonArrayContains(string columnSql, string parameter) => _list.JsonArrayContains(columnSql, parameter);
        public string JsonArrayLength(string columnSql) => _list.JsonArrayLength(columnSql);
        public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
            _list.JsonArrayElementLike(columnSql, patternParameter, literalParameter);
        public int MaximumParameters => 3;
    }

    private sealed class ParameterPreferringDialect : IRelationalMappingDialect
    {
        private readonly ListDialect _list = new();
        public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType) => _list.Read(path, shape, physicalType);
        public string QuoteIdent(string ident) => _list.QuoteIdent(ident);
        public string EscapeLike(string fragment) => fragment;
        public string Parameter(int index) => $"@p{index}";
        public string JsonArrayContains(string columnSql, string parameter) => _list.JsonArrayContains(columnSql, parameter);
        public string JsonArrayLength(string columnSql) => _list.JsonArrayLength(columnSql);
        public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
            _list.JsonArrayElementLike(columnSql, patternParameter, literalParameter);
        public bool PrefersSetParameters => true;
        public int MaximumParameters => 5;
        public string? CollectionMemberOf(string columnSql, IReadOnlyList<object?> members, SetMatch match, Type elementType,
            Func<object?, string> bind)
            => match == SetMatch.Any ? $"any({columnSql}, {bind(members.ToArray())})" : null;
        public string? MemberOf(string columnSql, IReadOnlyList<object?> members, bool negate, Func<object?, string> bind)
            => $"{columnSql} {(negate ? "<> ALL" : "= ANY")}({bind(members.ToArray())})";
    }

    private sealed class SetDialect : ListDialect, IRelationalMappingDialect
    {
        public string? MemberOf(string columnSql, IReadOnlyList<object?> members, bool negate, Func<object?, string> bind)
            => $"{columnSql} {(negate ? "<> ALL" : "= ANY")}({bind(members.ToArray())})";
    }
}
