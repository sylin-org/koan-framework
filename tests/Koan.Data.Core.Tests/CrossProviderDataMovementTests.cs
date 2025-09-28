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
/// Tests whether DataSetContext.With("provider") actually switches between providers
/// or just creates logical sets within the same provider
/// </summary>
public class CrossProviderDataMovementTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbFile;
    private readonly IServiceProvider _serviceProvider;

    public class TestEntity : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public CrossProviderDataMovementTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Koan-CrossProvider-Tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempDir);
        _dbFile = Path.Combine(_tempDir, "test.db");

        // Simple setup with both providers
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Koan:Data:Json:DirectoryPath", _tempDir),
                new KeyValuePair<string, string?>("Koan:Data:Sqlite:ConnectionString", $"Data Source={_dbFile}")
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddKoan();
        services.AddJsonAdapter(o => o.DirectoryPath = _tempDir);
        services.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={_dbFile}");

        _serviceProvider = services.BuildServiceProvider();

        // Simple initialization like other tests
        try { KoanEnv.TryInitialize(_serviceProvider); } catch { }
        Koan.Core.Hosting.App.AppHost.Current = _serviceProvider;

        var runtime = _serviceProvider.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
        runtime?.Discover();
        runtime?.Start();
    }

    [Fact]
    public async Task DataSetContext_With_Different_Providers_Should_Route_To_Different_Storage()
    {
        // Create test entity
        var testEntity = new TestEntity { Title = "Cross Provider Test", Value = 42 };

        // Save to "json" provider using DataSetContext
        string jsonId;
        using (var jsonContext = DataSetContext.With("json"))
        {
            var saved = await Data<TestEntity, string>.UpsertAsync(testEntity);
            jsonId = saved.Id;
            saved.Id.Should().NotBeNullOrWhiteSpace();
        }

        // Save to "sqlite" provider using DataSetContext
        string sqliteId;
        using (var sqliteContext = DataSetContext.With("sqlite"))
        {
            var saved = await Data<TestEntity, string>.UpsertAsync(testEntity);
            sqliteId = saved.Id;
            saved.Id.Should().NotBeNullOrWhiteSpace();
        }

        // Verify data exists in JSON context
        using (var jsonContext = DataSetContext.With("json"))
        {
            var jsonData = await Data<TestEntity, string>.All();
            jsonData.Should().ContainSingle();
            jsonData.First().Title.Should().Be("Cross Provider Test");
        }

        // Verify data exists in SQLite context
        using (var sqliteContext = DataSetContext.With("sqlite"))
        {
            var sqliteData = await Data<TestEntity, string>.All();
            sqliteData.Should().ContainSingle();
            sqliteData.First().Title.Should().Be("Cross Provider Test");
        }

        // KEY TEST: If this is true provider switching, the storage should be physically different
        // Check if JSON files were created in the temp directory
        var jsonFiles = Directory.GetFiles(_tempDir, "*.json");
        jsonFiles.Should().NotBeEmpty("JSON provider should create .json files");

        // Check if SQLite database file was created
        File.Exists(_dbFile).Should().BeTrue("SQLite provider should create .db file");
    }

    [Fact]
    public async Task Without_DataSetContext_Should_Use_Default_Provider()
    {
        // Save without any DataSetContext - should use default provider
        var entity = new TestEntity { Title = "Default Provider Test", Value = 99 };
        var saved = await Data<TestEntity, string>.UpsertAsync(entity);
        saved.Id.Should().NotBeNullOrWhiteSpace();

        // Retrieve without DataSetContext
        var all = await Data<TestEntity, string>.All();
        all.Should().ContainSingle();
        all.First().Title.Should().Be("Default Provider Test");
    }

    public void Dispose()
    {
        try
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}