using System;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Connector.Json;
using Koan.Data.Connector.Sqlite;
using Xunit;

namespace Koan.Data.Core.Tests;

/// <summary>
/// Tests entity-level default provider resolution and initialization caching
/// Demonstrates that different entities can have different default providers
/// </summary>
public class EntityDefaultProviderTests
{
    // Each test method gets its own isolated storage

    // Entity with explicit JSON provider
    [DataAdapter("json")]
    public class JsonEntity : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    // Entity with explicit SQLite provider
    [DataAdapter("sqlite")]
    public class SqliteEntity : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    // Entity with no explicit provider - should use framework default
    public class DefaultEntity : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private static IServiceProvider BuildTestServiceProvider()
    {
        TestHooks.ResetDataConfigs();

        var tempDir = Path.Combine(Path.GetTempPath(), "Koan-EntityProvider-Tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);
        var dbFile = Path.Combine(tempDir, "test.db");

        // Setup with multiple providers available
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Koan:Data:Json:DirectoryPath", tempDir),
                new KeyValuePair<string, string?>("Koan:Data:Sqlite:ConnectionString", $"Data Source={dbFile}")
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddKoan();

        var serviceProvider = services.BuildServiceProvider();

        // Initialize Koan environment
        try { KoanEnv.TryInitialize(serviceProvider); } catch { }
        Koan.Core.Hosting.App.AppHost.Current = serviceProvider;

        var runtime = serviceProvider.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
        runtime?.Discover();
        runtime?.Start();

        return serviceProvider;
    }

    [Fact]
    public async Task Entities_With_DataAdapter_Attribute_Use_Specified_Provider()
    {
        var serviceProvider = BuildTestServiceProvider();
        var tempDir = serviceProvider.GetRequiredService<IConfiguration>()["Koan:Data:Json:DirectoryPath"]!;
        var dbFile = serviceProvider.GetRequiredService<IConfiguration>()["Koan:Data:Sqlite:ConnectionString"]!.Replace("Data Source=", "");

        try
        {
        // JsonEntity should use JSON provider
        var jsonEntity = new JsonEntity { Title = "JSON Test", Value = 1 };
        var savedJson = await Data<JsonEntity, string>.UpsertAsync(jsonEntity);
        savedJson.Id.Should().NotBeNullOrWhiteSpace();

        // SqliteEntity should use SQLite provider
        var sqliteEntity = new SqliteEntity { Title = "SQLite Test", Value = 2 };
        var savedSqlite = await Data<SqliteEntity, string>.UpsertAsync(sqliteEntity);
        savedSqlite.Id.Should().NotBeNullOrWhiteSpace();

        // Verify they're using different storage
        // JSON should create .json files
        var jsonFiles = Directory.GetFiles(tempDir, "*.json");
        jsonFiles.Should().NotBeEmpty("JsonEntity should create JSON files");

        // SQLite should create .db file
        File.Exists(dbFile).Should().BeTrue("SqliteEntity should create SQLite database");

            // Verify data isolation - each entity type uses its own provider
            var jsonData = await Data<JsonEntity, string>.All();
            jsonData.Should().ContainSingle();
            jsonData.First().Title.Should().Be("JSON Test");

            var sqliteData = await Data<SqliteEntity, string>.All();
            sqliteData.Should().ContainSingle();
            sqliteData.First().Title.Should().Be("SQLite Test");
        }
        finally
        {
            // Cleanup
            if (serviceProvider is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (serviceProvider is IDisposable disposable)
                disposable.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(tempDir)!, true); } catch { }
        }
    }

    [Fact]
    public async Task Entity_Without_DataAdapter_Uses_Framework_Default()
    {
        var serviceProvider = BuildTestServiceProvider();
        try
        {
            var partition = "default-test-" + Guid.NewGuid().ToString("n");
            await Data<DefaultEntity, string>.DeleteAllAsync(partition);

            using (EntityContext.Partition(partition))
            {
                // DefaultEntity should use whatever provider the framework elects as default
                var uniqueTitle = $"Default Test {Guid.NewGuid():N}";
                var defaultEntity = new DefaultEntity { Title = uniqueTitle, Value = 3 };
                var saved = await Data<DefaultEntity, string>.UpsertAsync(defaultEntity);
                saved.Id.Should().NotBeNullOrWhiteSpace();

                var retrieved = await Data<DefaultEntity, string>.GetAsync(saved.Id);
                retrieved.Should().NotBeNull();
                retrieved!.Title.Should().Be(uniqueTitle);
                await Data<DefaultEntity, string>.DeleteAsync(saved.Id);
            }
        }
        finally
        {
            if (serviceProvider is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }
    }

    [Fact]
    public async Task Provider_Initialization_Should_Be_Cached_Per_Entity()
    {
        var serviceProvider = BuildTestServiceProvider();
        try
        {

        var dataService = serviceProvider.GetRequiredService<IDataService>();

        // First repository resolution should create and cache instance per entity type
        var jsonRepoFirst = dataService.GetRepository<JsonEntity, string>();
        var jsonRepoSecond = dataService.GetRepository<JsonEntity, string>();
        ReferenceEquals(jsonRepoFirst, jsonRepoSecond).Should().BeTrue("JsonEntity repository should be cached per entity");

        var sqliteRepoFirst = dataService.GetRepository<SqliteEntity, string>();
        var sqliteRepoSecond = dataService.GetRepository<SqliteEntity, string>();
        ReferenceEquals(sqliteRepoFirst, sqliteRepoSecond).Should().BeTrue("SqliteEntity repository should be cached per entity");

        ReferenceEquals(jsonRepoFirst, sqliteRepoFirst).Should().BeFalse("Different entity types should resolve distinct repositories");

        var jsonTitleA = "First " + Guid.NewGuid().ToString("n");
        var jsonTitleB = "Second " + Guid.NewGuid().ToString("n");

        await Data<JsonEntity, string>.DeleteAllAsync();
        await Data<SqliteEntity, string>.DeleteAllAsync();

        await Data<JsonEntity, string>.UpsertAsync(new JsonEntity { Title = jsonTitleA, Value = 1 });
        await Data<JsonEntity, string>.UpsertAsync(new JsonEntity { Title = jsonTitleB, Value = 2 });

        var allJson = await Data<JsonEntity, string>.All();
        allJson.Should().Contain(e => e.Title == jsonTitleA);
        allJson.Should().Contain(e => e.Title == jsonTitleB);

        var sqliteTitle = "SQLite " + Guid.NewGuid().ToString("n");
        await Data<SqliteEntity, string>.UpsertAsync(new SqliteEntity { Title = sqliteTitle, Value = 3 });

        var allSqlite = await Data<SqliteEntity, string>.All();
        allSqlite.Should().ContainSingle(e => e.Title == sqliteTitle);
        }
        finally
        {
            if (serviceProvider is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }
    }

    [Fact]
    public async Task Cross_Provider_Data_Movement_Between_Entity_Types()
    {
        var serviceProvider = BuildTestServiceProvider();
        try
        {

        // Demonstrate moving data between entity types with different providers

        // Create data in JSON provider via JsonEntity
        var jsonEntity = new JsonEntity { Title = "Move Me", Value = 42 };
        var savedJson = await Data<JsonEntity, string>.UpsertAsync(jsonEntity);

        // Move to SQLite provider by creating equivalent SqliteEntity
        var sqliteEntity = new SqliteEntity
        {
            Id = savedJson.Id, // Keep same ID
            Title = savedJson.Title,
            Value = savedJson.Value
        };
        var savedSqlite = await Data<SqliteEntity, string>.UpsertAsync(sqliteEntity);

        // Verify data exists in both providers
        var jsonData = await Data<JsonEntity, string>.All();
        jsonData.Should().ContainSingle(e => e.Title == "Move Me");

        var sqliteData = await Data<SqliteEntity, string>.All();
        sqliteData.Should().ContainSingle(e => e.Title == "Move Me");

        // Clean up original from JSON
        await Data<JsonEntity, string>.DeleteAsync(savedJson.Id);

        // Verify move completed
        var jsonDataAfter = await Data<JsonEntity, string>.All();
        jsonDataAfter.Should().BeEmpty();

            var sqliteDataAfter = await Data<SqliteEntity, string>.All();
            sqliteDataAfter.Should().ContainSingle(e => e.Title == "Move Me");
        }
        finally
        {
            if (serviceProvider is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }
    }

}
