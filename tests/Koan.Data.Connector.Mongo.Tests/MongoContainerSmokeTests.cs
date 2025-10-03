using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Testing;
using Testcontainers.MongoDb;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

public class MongoContainerSmokeTests : IAsyncLifetime
{
    private MongoDbContainer? _mongo;
    private string? _connString;
    private bool _available;

    public async Task InitializeAsync()
    {
        // Probe Docker first to avoid hard failures on environments without Docker
        var probe = await DockerEnvironment.ProbeAsync();
        if (!probe.Available)
        {
            _available = false;
            return;
        }
        _available = true;
        // Be nice to CI/dev boxes: disable Ryuk to reduce friction
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        try
        {
            _mongo = new MongoDbBuilder()
                .WithImage("mongo:7")
                .WithPortBinding(0, 27017)
                .Build();
            await _mongo.StartAsync();
            _connString = _mongo.GetConnectionString();
        }
        catch
        {
            // Docker might be installed but not usable (auth/config). Mark unavailable to skip tests.
            _available = false;
            if (_mongo is not null)
            {
                try { await _mongo.DisposeAsync(); } catch { }
                _mongo = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_mongo is not null) await _mongo.DisposeAsync();
    }

    private IServiceProvider BuildServices()
    {
        var dbName = "Koan-it-" + Guid.NewGuid().ToString("n");
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>("Koan:Data:Mongo:ConnectionString", _connString),
                new KeyValuePair<string,string?>("Koan:Data:Mongo:Database", dbName)
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddKoanDataCore();
        sc.AddMongoAdapter();
        sc.AddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        return sc.BuildServiceProvider();
    }

    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    [SkippableFact]
    public async Task Crud_roundtrip_against_container()
    {
        Skip.IfNot(_available, "Docker is not running or misconfigured; skipping container-based test.");
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

        await TestMongoTeardown.DropDatabaseAsync(sp);
    }
}

