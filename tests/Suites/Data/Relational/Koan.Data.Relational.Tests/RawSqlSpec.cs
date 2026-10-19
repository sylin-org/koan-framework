using AwesomeAssertions;
using Koan.Data.Relational.Ado;
using Xunit;

namespace Koan.Data.Relational.Tests;

/// <summary>
/// WEB-0074: the text rule under every raw provider query. It is deliberately lexer-free - it refuses the separator
/// wherever it appears - so these cases pin the places a lexer-based rule would have to be exactly right and a blunt
/// one cannot be wrong: a separator inside a literal, a quoted identifier, a comment, and every terminator shape.
/// </summary>
public sealed class RawSqlSpec
{
    [Theory]
    [InlineData("1 = 1")]
    [InlineData("\"Name\" = @name AND \"Age\" > @min")]
    [InlineData("koan_row.Id IS NOT NULL")]
    [InlineData("(SELECT 1) = 1 -- a trailing comment")]
    [InlineData("select \"Id\", \"Json\" from \"Doc\" where \"Id\" = @id")]
    public void A_text_with_no_separator_is_one_statement(string sql)
        => RawSql.RequireSingleStatement(sql);

    [Theory]
    [InlineData("1 = 1; DROP TABLE widgets")]
    [InlineData("1 = 1;")]
    [InlineData(";")]
    [InlineData("select 1; select 2")]
    [InlineData("1 = 1 ;-- comment")]
    [InlineData("1 = 1); DROP TABLE widgets; --")]
    [InlineData("x = 'a;b'")]
    [InlineData("\"a;b\" = 1")]
    [InlineData("1 = 1 /* ; */")]
    [InlineData("1 = 1\0")]
    [InlineData("1 = 1\0 DROP TABLE widgets")]
    public void A_separator_or_NUL_anywhere_is_refused_with_the_correction(string sql)
    {
        var act = () => RawSql.RequireSingleStatement(sql);

        act.Should().Throw<NotSupportedException>().WithMessage("*exactly one statement*parameters*");
    }

    [Fact]
    public void A_null_text_is_an_argument_error_not_a_pass()
    {
        var act = () => RawSql.RequireSingleStatement(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
