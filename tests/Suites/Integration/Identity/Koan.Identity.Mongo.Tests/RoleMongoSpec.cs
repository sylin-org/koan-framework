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
}
