using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Relational.Npgsql.Runtime;
using Koan.Data.Relational.Orchestration;
using Xunit;

namespace Koan.Data.Relational.Tests.Connectors;

/// <summary>
/// PostgreSQL index names are schema-wide and truncated to 63 bytes, and CREATE INDEX IF NOT EXISTS skips on a name
/// match, so a declared index could be silently missing (NK-056). These pin the rules the executor applies after each
/// create; the live proof is PostgresIndexNameSpec, which needs a server.
/// </summary>
public sealed class NpgsqlIndexNamesSpec
{
    private static readonly RelationalTableDefinition Customers = new("public", "Customer#p2", [], ["Id"]);

    private static RelationalIndexDefinition Index(string name, bool unique = false, int keys = 1, bool declared = false) => new(
        name,
        Enumerable.Range(0, keys).Select(index => new RelationalIndexPart(
            new PhysicalPath("Json", "K" + index), typeof(string), "text")),
        unique,
        primary: false,
        ttl: false,
        rewriteFree: true,
        nameDeclared: declared);

    private static RelationalTableDefinition Table(string name) => new("public", name, [], ["Id"]);

    [Fact]
    public void Effective_names_truncate_to_63_bytes_at_a_character_boundary()
    {
        NpgsqlIndexNames.Effective("IX_short").Should().Be("IX_short");
        NpgsqlIndexNames.Effective(new string('a', 70)).Should().Be(new string('a', 63));
        var multibyte = new string('a', 62) + "é" + "tail";
        NpgsqlIndexNames.Effective(multibyte).Should().Be(new string('a', 62), "a two-byte character never splits");
    }

    [Fact]
    public void A_derived_name_that_fits_is_unchanged()
        => NpgsqlIndexNames.Name(Customers, Index("IX_Customer#p2_Email"), partitioned: true)
            .Should().Be("IX_Customer#p2_Email");

    [Fact]
    public void Derived_names_over_the_limit_stay_distinct_instead_of_truncating_alike()
    {
        var prefix = "IX_" + new string('c', 60);
        var email = NpgsqlIndexNames.Name(Customers, Index(prefix + "_Email"), partitioned: true);
        var phone = NpgsqlIndexNames.Name(Customers, Index(prefix + "_Phone"), partitioned: true);

        email.Should().NotBe(phone);
        System.Text.Encoding.UTF8.GetByteCount(email).Should().BeLessThanOrEqualTo(63);
        System.Text.Encoding.UTF8.GetByteCount(phone).Should().BeLessThanOrEqualTo(63);
        NpgsqlIndexNames.Name(Customers, Index(prefix + "_Email"), partitioned: true).Should().Be(email, "a name is deterministic");
    }

    [Fact]
    public void A_declared_name_is_used_as_written_outside_a_partition()
        => NpgsqlIndexNames.Name(Table("Customer"), Index("UX_Email", declared: true), partitioned: false)
            .Should().Be("UX_Email");

    [Fact]
    public void A_declared_name_is_qualified_for_each_partition_table()
    {
        var first = NpgsqlIndexNames.Name(Table("Customer#p1"), Index("UX_Email", declared: true), partitioned: true);
        var second = NpgsqlIndexNames.Name(Table("Customer#p2"), Index("UX_Email", declared: true), partitioned: true);

        first.Should().StartWith("UX_Email_").And.NotBe(second);
        second.Should().StartWith("UX_Email_");
        NpgsqlIndexNames.Name(Table("Customer#p1"), Index("UX_Email", declared: true), partitioned: true)
            .Should().Be(first, "the same table always gets the same name");
    }

    [Fact]
    public void A_long_declared_name_is_qualified_within_63_bytes_and_still_distinct_per_table()
    {
        var name = "UX_" + new string('n', 70);
        var first = NpgsqlIndexNames.Name(Table("Customer#p1"), Index(name, declared: true), partitioned: true);
        var second = NpgsqlIndexNames.Name(Table("Customer#p2"), Index(name, declared: true), partitioned: true);

        first.Should().NotBe(second);
        System.Text.Encoding.UTF8.GetByteCount(first).Should().BeLessThanOrEqualTo(63);
        System.Text.Encoding.UTF8.GetByteCount(second).Should().BeLessThanOrEqualTo(63);
    }

    [Fact]
    public void Two_declared_names_that_truncate_alike_are_refused()
    {
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
        var prefix = "IX_" + new string('c', 60);
        NpgsqlIndexNames.Claim(claimed, Customers, prefix + "_Email");
        NpgsqlIndexNames.Claim(claimed, Customers, prefix + "_Email").Should().HaveLength(63, "the same name may be reclaimed");
        FluentActions.Invoking(() => NpgsqlIndexNames.Claim(claimed, Customers, prefix + "_Phone"))
            .Should().Throw<InvalidOperationException>().WithMessage("*both truncate*");
    }

    [Fact]
    public void A_name_owned_by_another_table_is_refused()
        => FluentActions.Invoking(() => NpgsqlIndexNames.Verify(Customers, Index("UX_Email", unique: true), "UX_Email",
                [new("Customer#p1", Unique: true, Keys: 1)]))
            .Should().Throw<InvalidOperationException>().WithMessage("*already belongs to table 'Customer#p1'*");

    [Fact]
    public void A_missing_index_is_refused()
        => FluentActions.Invoking(() => NpgsqlIndexNames.Verify(Customers, Index("IX_A"), "IX_A", []))
            .Should().Throw<InvalidOperationException>().WithMessage("*holds no index*");

    [Fact]
    public void A_declared_unique_index_served_by_a_non_unique_one_is_refused()
        => FluentActions.Invoking(() => NpgsqlIndexNames.Verify(Customers, Index("UX_Email", unique: true), "UX_Email",
                [new("Customer#p2", Unique: false, Keys: 1)]))
            .Should().Throw<InvalidOperationException>().WithMessage("*declared unique*");

    [Fact]
    public void A_different_key_count_is_refused()
        => FluentActions.Invoking(() => NpgsqlIndexNames.Verify(Customers, Index("IX_Pair", keys: 2), "IX_Pair",
                [new("Customer#p2", Unique: false, Keys: 1)]))
            .Should().Throw<InvalidOperationException>().WithMessage("*declares 2 key(s)*");

    [Fact]
    public void The_declared_index_on_its_own_table_is_accepted()
        => FluentActions.Invoking(() => NpgsqlIndexNames.Verify(Customers, Index("UX_Email", unique: true), "UX_Email",
                [new("Customer#p1", Unique: true, Keys: 1), new("Customer#p2", Unique: true, Keys: 1)]))
            .Should().NotThrow("a store that scopes names per table may list one row per table");
}
