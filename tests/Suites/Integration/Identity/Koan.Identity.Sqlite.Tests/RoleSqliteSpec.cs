using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Transactions;
using Koan.Identity.Roles;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Sqlite.Tests;

public sealed class RoleSqliteSpec
{
    [Fact] public async Task Sqlite_round_trips_collection_membership_and_compiles_a_bounded_bag()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-role-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("group:sqlite", "SQLite group", ["global:topic_read"], new Dictionary<string, string> { ["color"] = "green" });
            await roles.Add("group:sqlite", "person:sqlite");
            var bag = await roles.Bag("person:sqlite", true);
            bag.Tokens.Should().Contain(["group:sqlite", "global:topic_read"]);
            (await roles.Get("group:sqlite"))!.Metadata["color"].Should().Be("green");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact] public async Task A_role_change_inside_an_atomic_scope_refuses_before_any_write_and_after_commit_the_bag_is_correct()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-role-tx-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:tx", "Transactional", ["global:tx"]);
            var outside = await roles.Bag("person:tx", true);

            using (EntityContext.Transaction("role-change", TransactionGuarantee.Atomic))
            {
                var add = () => roles.Add("role:tx", "person:tx");
                (await add.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("EntityContext.Commit");
                var overwrite = () => roles.Overwrite("tangent:tx", OverwriteSubjectKind.Role, "role:tx", [], ["global:tx"]);
                await overwrite.Should().ThrowAsync<NotSupportedException>();
                await EntityContext.Commit();
            }

            (await roles.Get("role:tx"))!.Members.Should().BeEmpty("the refused change wrote nothing");
            (await roles.Bag("person:tx", true)).Should().BeSameAs(outside, "nothing was invalidated by a refused change");
            await roles.Add("role:tx", "person:tx");
            (await roles.Bag("person:tx", true)).Tokens.Should().Contain(["role:tx", "global:tx"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact] public async Task Sqlite_round_trips_an_overwrite_and_its_resolution()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-overwrite-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:guest", "Guest", ["global:view", "global:post"]);
            await roles.Add("role:guest", "person:guest");
            var until = DateTimeOffset.UtcNow.AddHours(1);
            await roles.Overwrite("tangent:workshop", OverwriteSubjectKind.Role, "role:guest", [], ["global:post"], until);

            var stored = (await roles.Overwrites("tangent:workshop")).Should().ContainSingle().Subject;
            stored.SubjectKind.Should().Be(OverwriteSubjectKind.Role);
            stored.Deny.Should().Equal("global:post");
            stored.ExpiresAt.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));

            var bag = await roles.Bag("person:guest", true);
            var here = bag.On("tangent:workshop", await roles.Overwrites("tangent:workshop"), DateTimeOffset.UtcNow);
            here.Holds("global:view").Should().BeTrue();
            here.Holds("global:post").Should().BeFalse();
            here.Explain("global:post").ContributingFacts.Should().ContainSingle().Which.Subject.Should().Be("role:guest");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Task<IntegrationHost> Start(string path) => KoanIntegrationHost.Configure().WithSettings(new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
        ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False",
    }).ConfigureServices(services => services.AddKoan()).StartAsync();
}
