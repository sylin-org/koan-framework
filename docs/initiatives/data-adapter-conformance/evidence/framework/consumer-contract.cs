// DAC-02 compile-contract fixture. Data contract tests compile this after DAC-03 introduces the surface.
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

internal static class ConsumerContract
{
    public static async Task Compile(IServiceCollection services, DateTimeOffset since, CancellationToken ct)
    {
        services.AddKoan(koan =>
        {
            koan.Data.Source("LegacyErp").Query(
                "orders.recent",
                query => query.Lane("Reports").Sql("select ORDER_NO as OrderId where CREATED_UTC >= @since")
                    .Parameter<DateTimeOffset>("since").MaxRecords(500).MaxBytes(4 * 1024 * 1024));

            koan.Data.Source("LegacyErp").Scalar<long>(
                "orders.recent-count",
                query => query.Lane("Reports").Sql("select count(*) where CREATED_UTC >= @since")
                    .Parameter<DateTimeOffset>("since").MaxValueBytes(64));

            koan.Data.Source("LegacyErp").Map<ContractCustomer>(map => map
                .Container("dbo", "CUSTOMER")
                .Key(customer => customer.Id).Name("CUSTOMER_NO")
                .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
                .Property(customer => customer.Profile).Object("PROFILE_JSON"));
        });

        var source = Data.Source("LegacyErp");
        var inspector = source.Inspect();
        var page = await inspector.Containers(100, null, ct);
        var reference = await inspector.Resolve(StorageAddress.From("dbo", "CUSTOMER"), ct);
        _ = await inspector.Describe(reference, ct);
        RecordSet sample = await inspector.Sample(reference, 20, ct);
        RecordSet recent = await source.Query("orders.recent", new { since }, ct);
        long count = await source.Scalar<long>("orders.recent-count", new { since }, ct);
        IReadOnlyList<ContractOrder> projected = recent.Project<ContractOrder>();
        _ = (page, sample, count, projected);
    }
}

public sealed class ContractCustomer : Entity<ContractCustomer, long>
{
    public ContractName Name { get; set; } = new();
    public ContractProfile Profile { get; set; } = new();
}

public sealed class ContractName { public string Full { get; set; } = ""; }
public sealed class ContractProfile { public string? Language { get; set; } }
public sealed record ContractOrder(long OrderId);
