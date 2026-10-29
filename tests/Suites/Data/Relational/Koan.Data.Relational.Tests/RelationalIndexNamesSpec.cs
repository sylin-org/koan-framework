using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Relational.Orchestration;
using Xunit;

namespace Koan.Data.Relational.Tests;

/// <summary>
/// DATA-0160: the one naming rule for stores whose index names span their containers (PostgreSQL schemas, SQLite
/// databases). A declared name is used as written on the entity's own table and qualified by a hash of the table name on
/// a partition table; a derived name already carries its table. The limit is the store's: PostgreSQL passes 63 bytes,
/// SQLite none.
/// </summary>
public sealed class RelationalIndexNamesSpec
{
    private static RelationalIndexDefinition Index(string name, bool declared) => new(
        name,
        [new RelationalIndexPart(new PhysicalPath("Json", "Topic"), typeof(string), "text")],
        unique: false,
        primary: false,
        ttl: false,
        rewriteFree: true,
        nameDeclared: declared);

    private static RelationalTableDefinition Table(string name) => new("main", name, [], ["Id"]);

    private static string Hash(string table) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(table)), 0, 4).ToLowerInvariant();

    [Fact]
    public void A_declared_name_is_used_as_written_on_the_entity_table()
        => RelationalIndexNames.Name(Table("Post"), Index("ix_post_topics", declared: true), partitioned: false)
            .Should().Be("ix_post_topics");

    [Fact]
    public void A_declared_name_on_a_partition_table_is_qualified_by_a_hash_of_the_table_name()
    {
        RelationalIndexNames.Name(Table("Post#audit"), Index("ix_post_topics", declared: true), partitioned: true)
            .Should().Be($"ix_post_topics_{Hash("Post#audit")}");
        RelationalIndexNames.Name(Table("Post#archive"), Index("ix_post_topics", declared: true), partitioned: true)
            .Should().Be($"ix_post_topics_{Hash("Post#archive")}").And.NotBe($"ix_post_topics_{Hash("Post#audit")}");
    }

    [Fact]
    public void A_derived_name_carries_its_table_and_is_not_qualified()
        => RelationalIndexNames.Name(Table("Post#audit"), Index("IX_Post#audit_Topic", declared: false), partitioned: true)
            .Should().Be("IX_Post#audit_Topic");

    [Fact]
    public void Without_a_store_limit_a_long_name_is_never_cut()
    {
        var name = "ix_" + new string('n', 90);
        RelationalIndexNames.Name(Table("Post#audit"), Index(name, declared: true), partitioned: true)
            .Should().Be($"{name}_{Hash("Post#audit")}");
    }

    [Fact]
    public void Under_a_store_limit_a_long_qualified_name_fits_and_stays_distinct_per_table()
    {
        var name = "ix_" + new string('n', 90);
        var audit = RelationalIndexNames.Name(Table("Post#audit"), Index(name, declared: true), partitioned: true, maximumBytes: 63);
        var archive = RelationalIndexNames.Name(Table("Post#archive"), Index(name, declared: true), partitioned: true, maximumBytes: 63);

        Encoding.UTF8.GetByteCount(audit).Should().BeLessThanOrEqualTo(63);
        Encoding.UTF8.GetByteCount(archive).Should().BeLessThanOrEqualTo(63);
        audit.Should().NotBe(archive);
    }
}
