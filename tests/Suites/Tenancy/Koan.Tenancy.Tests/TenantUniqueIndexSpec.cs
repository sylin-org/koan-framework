using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// DATA-0150 through a real <c>AddKoan()</c> boot with <c>Koan.Tenancy</c> on SQLite: a unique <c>[Index]</c> is unique
/// per tenant, and a duplicate is reported as a conflict that names neither tenant nor index.
/// </summary>
public sealed class TenantUniqueIndexSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string posture)
        => new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = posture };

    public sealed class Member : Entity<Member>
    {
        [Index(Unique = true)]
        public string Email { get; set; } = "";
    }

    [Fact(DisplayName = "DATA-0150: a unique index is unique per tenant under Koan.Tenancy")]
    public async Task Unique_index_is_unique_per_tenant()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _ = EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

        using (Tenant.Use("acme")) await new Member { Email = "a@x.test" }.Save();
        using (Tenant.Use("globex")) await new Member { Email = "a@x.test" }.Save();

        using (Tenant.Use("acme"))
        {
            var duplicate = async () => await new Member { Email = "a@x.test" }.Save();
            var refused = (await duplicate.Should().ThrowAsync<UniqueValueConflictException>()).Which;
            refused.Failure.Kind.Should().Be(DataFailureKind.Conflict);
            refused.Message.Should().NotContainAny("acme", "globex", "tenant", "IX_");
            (await Member.All()).Should().ContainSingle();
        }
        using (Tenant.Use("globex")) (await Member.All()).Should().ContainSingle();
    }
}
