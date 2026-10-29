using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Identity.Roles;
using Koan.Testing.Containers;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Mongo.Tests;

public sealed class RoleMongoSpec(MongoFixture fixture)
{
    [Fact] public async Task Mongo_round_trips_collection_membership_and_cache_invalidation()
    {
        await using var host = await KoanIntegrationHost.Configure().WithSettings(fixture.SettingsForBoot())
            .ConfigureServices(services => services.AddKoan()).StartAsync(TestContext.Current.CancellationToken);
        using var app = AppHost.PushScope(host.Services);
        var roles = host.Services.GetRequiredService<RoleCollection>();
        await roles.Define("role:mongo", "Mongo role", ["global:post_create"]);
        await roles.Add("role:mongo", "person:mongo");
        var before = await roles.Bag("person:mongo", true);
        await roles.SetPermissions("role:mongo", ["global:post_remove"]);
        var after = await roles.Bag("person:mongo", true);
        after.Should().NotBeSameAs(before);
        after.Tokens.Should().Contain("global:post_remove").And.NotContain("global:post_create");
    }

    [Fact] public async Task Mongo_round_trips_an_overwrite_bounds_it_and_resolves_it()
    {
        await using var host = await KoanIntegrationHost.Configure().WithSettings(fixture.SettingsForBoot())
            .ConfigureServices(services => services.AddKoan()).StartAsync(TestContext.Current.CancellationToken);
        using var app = AppHost.PushScope(host.Services);
        var roles = host.Services.GetRequiredService<RoleCollection>();
        var resource = $"board:{Guid.NewGuid():N}";
        await roles.Define("role:mongo-guest", "Mongo guest", ["global:view", "global:post"]);
        await roles.Add("role:mongo-guest", "person:mongo-guest");
        var until = DateTimeOffset.UtcNow.AddHours(1);
        await roles.Overwrite(resource, OverwriteSubjectKind.Role, "role:mongo-guest", [], ["global:post"], until);
        await roles.Overwrite(resource, OverwriteSubjectKind.Person, "person:mongo-guest", ["global:pin"], []);

        var rows = await roles.Overwrites(resource);
        rows.Should().HaveCount(2);
        rows.Single(row => row.SubjectKind == OverwriteSubjectKind.Role).ExpiresAt.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));
        (await roles.OverwritesOf(OverwriteSubjectKind.Person, "person:mongo-guest", TestContext.Current.CancellationToken))
            .Should().ContainSingle().Which.ResourceId.Should().Be(resource);

        var here = (await roles.Bag("person:mongo-guest", true)).On(resource, rows, DateTimeOffset.UtcNow);
        here.Permissions.Should().Equal("global:pin", "global:view");
        (await roles.RemoveOverwrite(resource, OverwriteSubjectKind.Person, "person:mongo-guest")).Should().BeTrue();
        (await roles.Overwrites(resource)).Should().ContainSingle();
    }
}
