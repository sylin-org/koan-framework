using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Core;
using Sora.Data.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Sora.Data.Mongo.Tests;

/// <summary>
/// Auto-provisions Mongo for tests using three strategies:
/// 1) Env connection string (SORA_TESTS_MONGO or MONGO_CONNECTION_STRING)
/// 2) Localhost:27017 if reachable
/// 3) Docker Testcontainers (mongo:7) if Docker is available
/// If none works, tests should call EnsureAvailable() which throws a SkipException.
/// </summary>
public sealed class MongoAutoFixture : IAsyncLifetime
{
    private MongoDbContainer? _mongo;
    public string? ConnectionString { get; private set; }
    public bool IsAvailable => !string.IsNullOrWhiteSpace(ConnectionString);
    public IServiceProvider? Services { get; private set; }

    public async Task InitializeAsync()
    {
        // 1) Env
        var env = Environment.GetEnvironmentVariable("SORA_TESTS_MONGO")
                  ?? Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (await CanPingAsync(env)) { ConnectionString = env; BuildServices(); return; }
        }

        // 2) Localhost
        var local = "mongodb://localhost:27017";
        if (await CanPingAsync(local)) { ConnectionString = local; BuildServices(); return; }

        // 3) Docker
        try
        {
            _mongo = new MongoDbBuilder()
                .WithImage("mongo:7")
                .WithPortBinding(0, 27017)
                .Build();
            await _mongo.StartAsync();
            ConnectionString = _mongo.GetConnectionString();
            BuildServices();
        }
        catch
        {
            // leave ConnectionString null; tests will skip
        }
    }

    public Task DisposeAsync()
    {
        if (_mongo is not null) return _mongo.DisposeAsync().AsTask();
        return Task.CompletedTask;
    }

    // Tests can check IsAvailable and early-return to effectively skip when Mongo isn't reachable.

    private static async Task<bool> CanPingAsync(string conn)
    {
        try
        {
            var client = new MongoClient(conn);
            var db = client.GetDatabase("admin");
            var res = await db.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1));
            return res is not null;
        }
        catch
        {
            return false;
        }
    }

    private void BuildServices()
    {
        if (!IsAvailable) return;
        var dbName = "sora-test-" + Guid.NewGuid().ToString("n");
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>("Sora:Data:Mongo:ConnectionString", ConnectionString),
                new KeyValuePair<string,string?>("Sora:Data:Mongo:Database", dbName)
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddMongoAdapter();
        // Provide naming resolver for StorageNameRegistry
        sc.AddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        Services = sc.BuildServiceProvider();
    }
}
