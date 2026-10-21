using Koan.Cache.Abstractions.Policies;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;

namespace Koan.Tests.Cache.Topology.Specs;

/// <summary>
/// NK-040: a mass delete invalidates the entity's cached entries. Every entry an entity cache writes carries one
/// entity tag, and DeleteAll, RemoveAll and row-changing instructions flush it, so a deleted row is never served
/// from the cache afterwards.
/// </summary>
public sealed class EntityCacheMassDeleteSpec
{
    public static TheoryData<string> MassDeletes => ["DeleteAll", "RemoveAll.Safe", "RemoveAll.Optimized", "RemoveAll.Fast", "Clear"];

    [Theory]
    [MemberData(nameof(MassDeletes))]
    public async Task A_mass_delete_stops_serving_cached_rows(string verb)
    {
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan()).StartAsync(TestContext.Current.CancellationToken);
        using var appScope = AppHost.PushScope(host.Services);
        using var route = EntityContext.With(adapter: "inmemory", partition: Guid.NewGuid().ToString("N"));

        var first = await new CachedNote { Title = "first" }.Save();
        var second = await new CachedNote { Title = "second" }.Save();
        (await CachedNote.Get(first.Id)).Should().NotBeNull();
        (await CachedNote.Get(second.Id)).Should().NotBeNull();

        switch (verb)
        {
            case "DeleteAll": await Data<CachedNote, string>.DeleteAll(); break;
            case "RemoveAll.Safe": await Data<CachedNote, string>.RemoveAll(RemoveStrategy.Safe); break;
            case "RemoveAll.Optimized": await Data<CachedNote, string>.RemoveAll(RemoveStrategy.Optimized); break;
            case "RemoveAll.Fast": await Data<CachedNote, string>.RemoveAll(RemoveStrategy.Fast); break;
            case "Clear": await Data<CachedNote, string>.Execute<int>(new Instruction(DataInstructions.Clear)); break;
        }

        using (EntityContext.NoCache()) (await CachedNote.Get(first.Id)).Should().BeNull("the store no longer holds the row");
        (await CachedNote.Get(first.Id)).Should().BeNull($"{verb} must invalidate the cached entry");
        (await CachedNote.Get(second.Id)).Should().BeNull($"{verb} must invalidate every cached entry of the entity");
    }
}

[Cacheable]
public sealed class CachedNote : Entity<CachedNote>
{
    public string Title { get; set; } = "";
}
