using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

public class MongoInstructionTests : IClassFixture<MongoAutoFixture>
{
    private readonly MongoAutoFixture _fx;
    public MongoInstructionTests(MongoAutoFixture fx) => _fx = fx;

    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private IServiceProvider BuildServices()
    {
        var dbName = "Koan-instr-" + Guid.NewGuid().ToString("n");
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>("Koan:Data:Mongo:ConnectionString", _fx.ConnectionString),
                new KeyValuePair<string,string?>("Koan:Data:Mongo:Database", dbName)
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddKoanDataCore();
        sc.AddMongoAdapter();
        // Provide naming resolver for StorageNameRegistry
        sc.AddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task EnsureCreated_Insert_Then_Clear_Works()
    {
        if (!_fx.IsAvailable) return; // effectively skip when Mongo isn't available
        var sp = BuildServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var ensured = await data.Execute<Todo, bool>(new Instruction("data.ensureCreated"));
        ensured.Should().BeTrue();

        await repo.UpsertAsync(new Todo { Title = "a" });
        await repo.UpsertAsync(new Todo { Title = "b" });

        (await repo.QueryAsync(null)).Count.Should().Be(2);

        var cleared = await data.Execute<Todo, int>(new Instruction("data.clear"));
        cleared.Should().Be(2);

        (await repo.QueryAsync(null)).Should().BeEmpty();
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }
}

