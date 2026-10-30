using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Impersonation;
using Koan.Web.Auth.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Sqlite.Tests;

/// <summary>
/// What every <c>/me</c> and every sign-in reads about a person is found through an index: their external links, their
/// email factors, their sessions and who may impersonate them. As in <see cref="RoleSqliteSpec"/>, the connector hands
/// back no command text, so the plans explained are of SQL in the shape it issues, and the reads the host makes first
/// prove those rows exist and come back.
/// </summary>
public sealed class IdentityLookupIndexSpec
{
    [Fact]
    public async Task Reads_of_a_persons_rows_by_their_id_are_planned_with_their_indexes()
    {
        const string person = "person:lookup";
        var path = Path.Combine(Path.GetTempPath(), $"koan-identity-lookup-{Guid.CreateVersion7():n}.db");
        try
        {
            await using (var host = await RoleSqliteSpec.Start(path))
            {
                using var app = AppHost.PushScope(host.Services);

                // A sign-in: the store links the provider identity, and reconciliation writes the email factor.
                var store = host.Services.GetRequiredService<IExternalIdentityStore>();
                await store.Link(new ExternalIdentity
                {
                    UserId = person,
                    Provider = "probe",
                    ProviderKeyHash = "hash",
                    ClaimsJson = """{ "email": "Lookup@Example.com", "email_verified": true }""",
                });

                // The read every /me makes.
                (await store.GetByUser(person)).Should().ContainSingle().Which.Provider.Should().Be("probe");

                var factor = (await IdentityEmail.Query(row => row.IdentityId == person)).Should().ContainSingle().Subject;
                factor.Id.Should().Be(IdentityEmail.KeyFor(person, "lookup@example.com"), "the factor is read back by the id composed from the person and the address");
                factor.Primary.Should().BeTrue();

                await new Session { IdentityId = person }.Save();
                await new ImpersonationGrant { Actor = "person:operator", Target = person, Reason = "probe" }.Save();
                (await host.Services.GetRequiredService<ImpersonationService>().ForTargetAsync(person)).Should().ContainSingle();
            }

            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();

            await PlannedWithIndex(connection, nameof(ExternalIdentityLink), nameof(ExternalIdentityLink.IdentityId), person);
            await PlannedWithIndex(connection, nameof(IdentityEmail), nameof(IdentityEmail.IdentityId), person);
            await PlannedWithIndex(connection, nameof(Session), nameof(Session.IdentityId), person);
            await PlannedWithIndex(connection, nameof(ImpersonationGrant), nameof(ImpersonationGrant.Target), person);
            await PlannedWithIndex(connection, nameof(ImpersonationGrant), nameof(ImpersonationGrant.Actor), "person:operator");
        }
        finally { RoleSqliteSpec.Delete(path); }
    }

    private static async Task PlannedWithIndex(SqliteConnection connection, string entity, string property, string value)
    {
        var table = (string?)await RoleSqliteSpec.Scalar(connection, $"SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE '%{entity}%'");
        table.Should().NotBeNull($"{entity} rows have a table");

        var plan = await RoleSqliteSpec.Plan(connection, $"""
            SELECT "Id" FROM "{table}" AS koan_row
            WHERE json_extract(koan_row."Json", '$."{property}"') = @p0
            """, value);

        var indexes = (await RoleSqliteSpec.Indexes(connection, table!))
            .Where(index => index.Sql.Contains(property, StringComparison.Ordinal))
            .Select(index => index.Name)
            .ToArray();
        indexes.Should().NotBeEmpty($"{entity}.{property} declares an index");
        plan.Should().Contain(detail => indexes.Any(index => detail.Contains(index, StringComparison.Ordinal)),
            $"a read of {entity} by {property} is planned with its index");
    }
}
