using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Core.Semantics.Contributions;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Lexical;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0162: the <c>data:lexical</c> probe holds no tenant, so a tenant-scoped entity's index is reported unmeasured and
/// the report is Unknown, never a permanent Degraded; inside the tenant, <c>Entity.Lexical.Status()</c> measures it.
/// </summary>
public sealed class SqliteLexicalReadinessUnderTenancySpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Fact(DisplayName = "Sqlite: a tenant-scoped lexical index is unmeasured by the probe, not degraded")]
    public async Task A_tenant_scoped_lexical_index_reads_as_unknown()
    {
        RequireBackingStore();
        LexicalTenancy.Segmented = true;
        try
        {
            await using var host = await BootAsync();
            using var _ = Lease(NewPartition("lexical-tenancy"));
            using (LexicalTenancy.Use("acme"))
            {
                await new TenantNote { Title = "scoped" }.Save();
                (await TenantNote.Lexical.Status()).Current.Should().BeTrue("inside the tenant the index is measured");
            }

            var contributor = host.Services.GetServices<IHealthContributor>().Single(candidate => candidate.Name == "data:lexical");
            var report = await contributor.Check();
            Output.WriteLine($"{report.State}: {report.Description}");
            Output.WriteLine(string.Join(Environment.NewLine, report.Data!.Select(pair => $"{pair.Key}={pair.Value}")));
            report.Data.Should().ContainKey(nameof(TenantNote)).WhoseValue.Should().Be("unmeasured:segmented");
            report.State.Should().NotBe(HealthState.Degraded);
        }
        finally
        {
            LexicalTenancy.Segmented = false;
        }
    }

    public sealed class TenantNote : Entity<TenantNote>
    {
        [Lexical] public string Title { get; set; } = "";
    }
}

/// <summary>A row-scoped tenant dimension over <see cref="SqliteLexicalReadinessUnderTenancySpec.TenantNote"/> only,
/// contributed to a host composed while <see cref="Segmented"/> is set.</summary>
public sealed class LexicalTenancyModule : KoanModule, IContributeTo<SegmentationContributionTarget>
{
    public void Contribute(SegmentationContributionTarget target)
    {
        if (!LexicalTenancy.Segmented) return;
        target.Require(
            "tenant",
            static () => LexicalTenancy.Current is { Length: > 0 } tenant
                ? SegmentationValue.For(tenant)
                : SegmentationValue.Missing,
            static type => type == typeof(SqliteLexicalReadinessUnderTenancySpec.TenantNote),
            "Run the TenantNote operation inside LexicalTenancy.Use(...).");
    }
}

public static class LexicalTenancy
{
    private static readonly AsyncLocal<string?> Tenant = new();

    public static bool Segmented { get; set; }

    public static string? Current => Tenant.Value;

    public static IDisposable Use(string tenant)
    {
        var previous = Tenant.Value;
        Tenant.Value = tenant;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => Tenant.Value = previous;
    }
}
