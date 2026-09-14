using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
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
            await using var host = await KoanIntegrationHost.Configure().WithSettings(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False",
            }).ConfigureServices(services => services.AddKoan()).StartAsync();
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
}
