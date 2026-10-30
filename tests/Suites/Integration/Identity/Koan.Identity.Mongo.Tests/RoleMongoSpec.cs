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

    [Fact] public async Task Mongo_round_trips_an_overwrite_lists_it_and_resolves_it()
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

        var rows = (await roles.Overwrites(resource)).Items;
        rows.Should().HaveCount(2);
        rows.Single(row => row.SubjectKind == OverwriteSubjectKind.Role).ExpiresAt.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));
        (await roles.OverwritesOf(OverwriteSubjectKind.Person, "person:mongo-guest", ct: TestContext.Current.CancellationToken)).Items
            .Should().Contain(row => row.ResourceId == resource);

        var bag = await roles.Bag("person:mongo-guest", true);
        var here = bag.On(resource, await roles.OverwritesFor(bag, resource), DateTimeOffset.UtcNow);
        here.Permissions.Should().Equal("global:pin", "global:view");
        (await roles.RemoveOverwrite(resource, OverwriteSubjectKind.Person, "person:mongo-guest")).Should().BeTrue();
        (await roles.Overwrites(resource)).Items.Should().ContainSingle();
    }

    [Fact] public async Task Mongo_pages_a_roles_members_and_reads_a_persons_roles_by_person()
    {
        await using var host = await KoanIntegrationHost.Configure().WithSettings(fixture.SettingsForBoot())
            .ConfigureServices(services => services.AddKoan()).StartAsync(TestContext.Current.CancellationToken);
        using var app = AppHost.PushScope(host.Services);
        var roles = host.Services.GetRequiredService<RoleCollection>();
        var key = $"role:mongo-paged:{Guid.NewGuid():N}";
        await roles.Define(key, "Paged", ["global:read"]);
        var people = Enumerable.Range(0, 12).Select(i => $"person:mongo-paged:{key}:{i:D2}").ToArray();
        foreach (var person in people.Reverse()) await roles.Add(key, person);

        var read = new List<string>();
        for (string? after = null; ;)
        {
            var page = await roles.Members(key, after, take: 5);
            read.AddRange(page.Items.Select(member => member.PersonId));
            if ((after = page.Next) is null) break;
        }

        read.Should().Equal(people, "each member once, in person order");
        (await roles.ForPerson(people[3])).Should().ContainSingle().Which.Key.Should().Be(key);
        (await roles.Remove(key, people[3])).Should().BeTrue();
        (await roles.Bag(people[3], true)).InRole(key).Should().BeFalse();
    }
}
