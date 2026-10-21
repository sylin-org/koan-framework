using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;
using Koan.Data.Relational.Npgsql.Runtime;
using Xunit;

namespace Koan.Data.Relational.Tests.Connectors;

/// <summary>
/// PostgreSQL reads a document scalar as text (<c>#&gt;&gt;</c>), and Npgsql binds a Guid as uuid and a DateTime as
/// timestamp, for which PostgreSQL has no comparison with text (NK-052). Both sides now compare as document text. These
/// pin the generated SQL; the live proof is PostgresIdentityValueConvergenceSpec, which needs a server.
/// </summary>
public sealed class NpgsqlDocumentComparisonSpec
{
    private static readonly Guid Customer = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static (string Sql, IReadOnlyList<object?> Parameters) Translate(string field, FilterOperator op, object? value)
    {
        var plan = RelationalManagedMapping.Compile<Order>("Default", StorageAddress.From("public", "ORDERS"));
        return new SqlFilterTranslator(new NpgsqlDialect(), plan)
            .Translate(Filter.On(FieldPath.Of(field), op, FilterValue.Of(value)));
    }

    [Fact]
    public void A_guid_comparand_binds_as_document_text()
    {
        var (sql, parameters) = Translate(nameof(Order.CustomerId), FilterOperator.Eq, Customer);
        sql.Should().Be("((\"Json\" #>> '{CustomerId}') = @p0)", "the read stays the indexed expression");
        parameters.Should().Equal("0f8fad5b-d9cb-469f-a165-70867728950e");
    }

    [Fact]
    public void A_datetime_compares_as_fixed_width_wall_clock_text()
    {
        var at = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc).AddTicks(1234567);
        var (sql, parameters) = Translate(nameof(Order.PlacedAt), FilterOperator.Gt, at);
        sql.Should().StartWith("((substr((\"Json\" #>> '{PlacedAt}'), 1, 19) || '.' ||")
            .And.EndWith(" COLLATE \"C\" > @p0)");
        parameters.Should().Equal("2026-03-01T09:30:00.1234567");
    }

    [Fact]
    public void Guid_and_datetime_text_match_what_the_serializer_writes()
    {
        RelationalDocumentText.Comparand(Customer).Should().Be(Newtonsoft.Json.JsonConvert.SerializeObject(Customer).Trim('"'));
        RelationalDocumentText.Comparand(new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Local))
            .Should().Be("2026-03-01T09:30:00.0000000", "the oracle compares ticks and ignores Kind");
    }

    public sealed class Order
    {
        public string Id { get; set; } = "";
        public Guid CustomerId { get; set; }
        public DateTime PlacedAt { get; set; }
    }
}
