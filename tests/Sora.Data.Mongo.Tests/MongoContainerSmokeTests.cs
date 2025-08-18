using System;
using System.Threading.Tasks;
using Testcontainers.MongoDb;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Mongo;
using Xunit;

namespace Sora.Data.Mongo.Tests;

public class MongoContainerSmokeTests : IAsyncLifetime
{
    private MongoDbContainer? _mongo;
    private string? _connString;

    public async Task InitializeAsync()
    {
        _mongo = new MongoDbBuilder()
            .WithImage("mongo:7")
            .WithPortBinding(0, 27017)
            .Build();
    await _mongo.StartAsync();
    _connString = _mongo.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
    if (_mongo is not null) await _mongo.DisposeAsync();
    }

    private IServiceProvider BuildServices()
    {
        var dbName = "sora-it-" + Guid.NewGuid().ToString("n");
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>("Sora:Data:Mongo:ConnectionString", _connString),
                new KeyValuePair<string,string?>("Sora:Data:Mongo:Database", dbName)
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddMongoAdapter();
        sc.AddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        return sc.BuildServiceProvider();
    }

    public class Todo : IEntity<string>
    {
        [Sora.Data.Abstractions.Annotations.Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    [Fact]
    public async Task Crud_roundtrip_against_container()
    {
        var sp = BuildServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var created = await repo.UpsertAsync(new Todo { Title = "hello" });
        created.Id.Should().NotBeNullOrWhiteSpace();

        var found = await repo.GetAsync(created.Id);
        found!.Title.Should().Be("hello");

        found.Title = "updated";
        await repo.UpsertAsync(found);

        var again = await repo.GetAsync(created.Id);
        again!.Title.Should().Be("updated");

        (await repo.DeleteAsync(created.Id)).Should().BeTrue();
        (await repo.GetAsync(created.Id)).Should().BeNull();

        // Smoke ping via low-level driver too
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoOptions>>().Value;
        var client = new MongoClient(opts.ConnectionString);
        var db = client.GetDatabase(opts.Database);
        var ping = await db.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1));
        ping.Should().NotBeNull();
    }
}
