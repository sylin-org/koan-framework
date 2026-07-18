using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Semantics;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

public sealed class SegmentationContributionSpec
{
    private sealed class TenantRecord : Entity<TenantRecord>;

    [HostScoped]
    private sealed class ControlRecord : Entity<ControlRecord>;

    [Fact]
    public void AddKoan_compiles_exactly_one_tenant_dimension_from_reference_intent()
    {
        var services = new ServiceCollection();

        services.AddKoan();
        using var provider = services.BuildServiceProvider();
        var plan = provider.GetRequiredService<SegmentationPlan>();

        plan.Dimensions.Should().ContainSingle();
        plan.Dimensions[0].Id.Should().Be("tenant");
        plan.Dimensions[0].Owner.Should().Be("Sylin.Koan.Tenancy");
        plan.For(typeof(TenantRecord)).DimensionIds.Should().Equal("tenant");
        plan.For(typeof(ControlRecord)).IsEmpty.Should().BeTrue();

        // Tenancy owns only the logical dimension. Data compiles its physical shared-row field from the host plan;
        // the former Tenancy-owned static managed-field registration must stay absent.
        ManagedFieldRegistry.All.Should().NotContain(field => field.StorageName == "__koan_tenant");
        provider.GetRequiredService<DataSegmentationPlan>()
            .For(typeof(TenantRecord)).Fields.Should().ContainSingle()
            .Which.Should().Be(new DataSegmentationField("tenant", "__koan_tenant", typeof(string)));
    }

    [Fact]
    public void Tenant_business_scope_binds_concrete_host_and_missing_states()
    {
        var services = new ServiceCollection();
        services.AddKoan();
        using var provider = services.BuildServiceProvider();
        var scope = provider.GetRequiredService<SegmentationPlan>().For(typeof(TenantRecord));

        using (Tenant.Use("tenant-a"))
        {
            scope.Bind("entity read").Should().ContainSingle()
                .Which.Should().Be(new SegmentationBinding("tenant", "tenant-a"));
        }

        using (Tenant.None())
        {
            var hostRead = () => scope.Bind("entity read");
            hostRead.Should().Throw<SegmentationRequiredException>();
            provider.GetRequiredService<SegmentationPlan>()
                .For(typeof(ControlRecord)).Bind("control-plane read").Should().BeEmpty();
        }

        var bind = () => scope.Bind("entity read");
        bind.Should().Throw<SegmentationRequiredException>()
            .WithMessage("*trusted tenant context*[HostScoped]*explicit host scope*");
    }

    [Fact]
    public async Task Real_host_reports_one_value_free_receipt_for_each_active_state_pillar()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(withLocalStorage: true);
        runtime.ResetEntityCaches();

        var facts = runtime.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
            .Where(fact => fact.Code == Constants.Diagnostics.Codes.SegmentationRealizationActive)
            .ToArray();

        facts.Select(fact => fact.Subject).Should().Equal(
            "segmentation:cache",
            "segmentation:communication",
            "segmentation:data",
            "segmentation:storage");
        facts.Should().OnlyContain(fact =>
            fact.Summary.Contains("enforced-or-rejected", StringComparison.Ordinal)
            && !fact.Summary.Contains("tenant-a", StringComparison.Ordinal));
    }
}
