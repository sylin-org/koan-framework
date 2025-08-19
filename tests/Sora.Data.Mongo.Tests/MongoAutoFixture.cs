using System;
using System.Threading.Tasks;
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

    public async Task InitializeAsync()
    {
        // 1) Env
        var env = Environment.GetEnvironmentVariable("SORA_TESTS_MONGO")
                  ?? Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (await CanPingAsync(env)) { ConnectionString = env; return; }
        }

        // 2) Localhost
        var local = "mongodb://localhost:27017";
        if (await CanPingAsync(local)) { ConnectionString = local; return; }

        // 3) Docker
        try
        {
            _mongo = new MongoDbBuilder()
                .WithImage("mongo:7")
                .WithPortBinding(0, 27017)
                .Build();
            await _mongo.StartAsync();
            ConnectionString = _mongo.GetConnectionString();
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
}
