using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// DATA-0149 through a real <c>AddKoan()</c> boot with row-scoped tenancy on SQLite. An exact transfer keeps every
/// stored value, OnSave timestamps included, and still reads and writes only inside the caller's tenant: the moved row
/// keeps its tenant, and another tenant's rows are neither read nor reached.
/// </summary>
public sealed class TenantTransferSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string posture)
        => new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = posture };

    public sealed class Ledger : Entity<Ledger>
    {
        public string Title { get; set; } = "";

        [Timestamp(OnSave = true)]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    [Fact(DisplayName = "DATA-0149: an exact Move keeps stored timestamps and stays inside the caller's tenant")]
    public async Task Exact_move_keeps_stored_values_inside_the_callers_tenant()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        var hot = "hot" + Guid.CreateVersion7().ToString("n");
        var archive = "archive" + Guid.CreateVersion7().ToString("n");

        Ledger acme, globex;
        using (Tenant.Use("acme")) using (EntityContext.Partition(hot)) acme = await new Ledger { Title = "acme" }.Save();
        using (Tenant.Use("globex")) using (EntityContext.Partition(hot)) globex = await new Ledger { Title = "globex" }.Save();
        await Task.Delay(30);

        using (Tenant.Use("acme"))
        {
            var result = await Ledger.Move().From(partition: hot).To(partition: archive).Run();
            result.ReadCount.Should().Be(1, "the transfer reads only the caller's tenant");
            result.CopiedCount.Should().Be(1);
            result.DeletedCount.Should().Be(1);

            using (EntityContext.Partition(archive))
            {
                var moved = (await Ledger.Get(acme.Id))!;
                moved.UpdatedAt.Should().Be(acme.UpdatedAt, "an exact transfer keeps the OnSave timestamp it read");
                (await Ledger.Get(globex.Id)).Should().BeNull();
            }
        }

        using (Tenant.Use("globex"))
        {
            using (EntityContext.Partition(archive)) (await Ledger.All()).Should().BeEmpty("the moved row kept acme's tenant");
            using (EntityContext.Partition(hot)) (await Ledger.Get(globex.Id))!.UpdatedAt.Should().Be(globex.UpdatedAt);
        }
    }
}
