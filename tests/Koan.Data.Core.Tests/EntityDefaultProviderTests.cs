using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Json;
using Koan.Data.Sqlite;
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
        // DefaultEntity should use whatever provider the framework elects as default
        var defaultEntity = new DefaultEntity { Title = "Default Test", Value = 3 };
        var saved = await Data<DefaultEntity, string>.UpsertAsync(defaultEntity);
        saved.Id.Should().NotBeNullOrWhiteSpace();

            var all = await Data<DefaultEntity, string>.All();
            all.Should().ContainSingle();
            all.First().Title.Should().Be("Default Test");
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

        // This test will verify that provider initialization happens only once per entity type
        // and that the initialization state is cached in AggregateBags

        // First access to JsonEntity - should trigger initialization
        var entity1 = new JsonEntity { Title = "First", Value = 1 };
        await Data<JsonEntity, string>.UpsertAsync(entity1);

        // Check initialization state
        var jsonConfig = AggregateConfigs.Get<JsonEntity, string>(serviceProvider);
        jsonConfig.IsProviderInitialized().Should().BeTrue("JSON provider should be initialized after first use");
        jsonConfig.GetProviderInitializedAt().Should().NotBeNull("Initialization timestamp should be recorded");
        jsonConfig.Provider.Should().Be("json", "JsonEntity should use JSON provider");

        var jsonInitTime = jsonConfig.GetProviderInitializedAt();

        // Second access to JsonEntity - should reuse initialized provider
        var entity2 = new JsonEntity { Title = "Second", Value = 2 };
        await Data<JsonEntity, string>.UpsertAsync(entity2);

        // Initialization state should remain the same (cached)
        var jsonConfigAfter = AggregateConfigs.Get<JsonEntity, string>(serviceProvider);
        jsonConfigAfter.GetProviderInitializedAt().Should().Be(jsonInitTime, "Initialization should be cached, not repeated");

        // Both should be persisted
        var allJson = await Data<JsonEntity, string>.All();
        allJson.Should().HaveCount(2);

        // First access to SqliteEntity - should trigger separate initialization
        var sqliteEntity = new SqliteEntity { Title = "SQLite", Value = 3 };
        await Data<SqliteEntity, string>.UpsertAsync(sqliteEntity);

        // Check SqliteEntity has separate initialization
        var sqliteConfig = AggregateConfigs.Get<SqliteEntity, string>(serviceProvider);
        sqliteConfig.IsProviderInitialized().Should().BeTrue("SQLite provider should be initialized");
        sqliteConfig.Provider.Should().Be("sqlite", "SqliteEntity should use SQLite provider");
        sqliteConfig.GetProviderInitializedAt().Should().NotBe(jsonInitTime, "Different entities should have separate initialization");

        var allSqlite = await Data<SqliteEntity, string>.All();
        allSqlite.Should().ContainSingle();

            // Verify data isolation - JsonEntity and SqliteEntity use different providers
            allJson.Should().OnlyContain(e => e.Title.StartsWith("First") || e.Title.StartsWith("Second"));
            allSqlite.Should().OnlyContain(e => e.Title == "SQLite");
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