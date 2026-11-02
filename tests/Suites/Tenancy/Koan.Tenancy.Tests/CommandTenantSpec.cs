using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// DATA-0167: a command runs in its sender's context, so a tenant's command writes that tenant's rows, however the queue's own flow
/// is scoped, and a sequence over a tenant-scoped entity counts within the tenant.
/// </summary>
public sealed class CommandTenantSpec
{
    public sealed class TenantSlip : Entity<TenantSlip>
    {
        public static readonly Sequence Numbers = Sequence.Of<TenantSlip>(slip => slip.Number);

        [Index(Name = "ix_tenant_slip_number", Unique = true)] public long Number { get; set; }
    }

    public sealed record NumberATenantSlip : ICommand<TenantSlip>
    {
        public async Task<Outcome<TenantSlip>> Run(CancellationToken ct) =>
            new Outcome<TenantSlip>.Done(await new TenantSlip { Number = await TenantSlip.Numbers.Next(ct) }.Save(ct));
    }

    private static TenantSlip Done(Outcome<TenantSlip> outcome) => outcome.Should().BeOfType<Outcome<TenantSlip>.Done>().Which.Result;

    [Fact]
    public async Task A_tenants_command_writes_that_tenants_rows_and_numbers_within_the_tenant()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" });
        var bound = TimeSpan.FromSeconds(30);

        TenantSlip acme, globex;
        using (Tenant.Use("acme")) acme = Done(await new NumberATenantSlip().Send().WaitAsync(bound));
        using (Tenant.Use("globex")) globex = Done(await new NumberATenantSlip().Send().WaitAsync(bound));

        (acme.Number, globex.Number).Should().Be((1L, 1L));
        using (Tenant.Use("acme"))
        {
            (await TenantSlip.Get(acme.Id)).Should().NotBeNull();
            (await TenantSlip.Get(globex.Id)).Should().BeNull();
        }
        using (Tenant.Use("globex"))
        {
            (await TenantSlip.Get(globex.Id)).Should().NotBeNull();
            (await TenantSlip.Get(acme.Id)).Should().BeNull();
        }
    }
}
