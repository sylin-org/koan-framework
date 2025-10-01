using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Models;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Connector.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.MongoDb;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Data.Backup.Tests.RealWorld;

/// <summary>
/// Full-stack test that validates backup/restore with real Entity models against real databases
/// </summary>
public class FullStackBackupRestoreTests : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDirectory;
    private readonly string _sqliteConnectionString;

    // Test containers for real databases
    private readonly MongoDbContainer _mongoContainer;

    private IHost? _host;
    private readonly List<TestEntityUser> _originalUsers = new();
    private readonly List<TestEntityProduct> _originalProducts = new();

    public FullStackBackupRestoreTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"koan-backup-fullstack-{Guid.CreateVersion7()}");
        Directory.CreateDirectory(_tempDirectory);

        _sqliteConnectionString = $"Data Source={Path.Combine(_tempDirectory, "test.db")}";

        // Setup test containers
        _mongoContainer = new MongoDbBuilder()
            .WithImage("mongo:7.0")
            .WithPortBinding(27017, true)
            .Build();

    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("Starting database containers...");

        // Start containers
        await _mongoContainer.StartAsync();

        var mongoConnectionString = _mongoContainer.GetConnectionString();

        _output.WriteLine($"Database connections:");
        _output.WriteLine($"   SQLite: {_sqliteConnectionString}");
        _output.WriteLine($"   MongoDB: {mongoConnectionString}");

        // Setup Koan host with real database providers
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Configure database providers
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Koan:Data:Sqlite:ConnectionString"] = _sqliteConnectionString,
                        ["Koan:Data:Mongo:ConnectionString"] = mongoConnectionString,
                        ["Koan:Data:Mongo:DatabaseName"] = "test_backup_db",
                    })
                    .Build();
                services.AddSingleton<IConfiguration>(config);

                // Add logging
                services.AddLogging(builder => builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Information));

                // Add Koan framework (backup services auto-registered via KoanAutoRegistrar)
                services.AddKoan();
                services.AddSingleton<Koan.Data.Abstractions.Naming.IStorageNameResolver, Koan.Data.Abstractions.Naming.DefaultStorageNameResolver>();

                // Add data providers
                services.AddSqliteAdapter();
                services.AddMongoAdapter();

                // Optional: Configure backup/restore options (auto-registered services)
                services.Configure<Koan.Data.Backup.Models.BackupRestoreOptions>(options =>
                {
                    options.DefaultStorageProfile = "test";
                    options.DefaultBatchSize = 50;
                    options.WarmupEntitiesOnStartup = true;
                });

                // Add file-based storage service for backups
                services.AddSingleton<Koan.Storage.Abstractions.IStorageService>(provider =>
                    new FileBasedStorageService(_tempDirectory, provider.GetRequiredService<ILogger<FileBasedStorageService>>()));
            });

        _host = hostBuilder.Build();
        await _host.StartAsync();

        // Set up AppHost for ambient service access
        Koan.Core.Hosting.App.AppHost.Current = _host.Services;

        // Initialize Koan runtime
        try { Koan.Core.KoanEnv.TryInitialize(_host.Services); } catch { }
        var runtime = _host.Services.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
        runtime?.Discover();
        runtime?.Start();

        _output.WriteLine("Koan host started with database providers");

        // Warmup the data layer
        await WarmupDataLayer();
    }

    [Fact]
    public async Task Full_Stack_Backup_Restore_With_Real_Databases_Should_Work()
    {
        _output.WriteLine("\nStarting full-stack backup/restore test...");

        // Step 1: Create and save real data to databases
        await CreateAndSaveTestData();

        // Step 2: Verify data was saved to databases
        await VerifyDataInDatabases();

        // Step 3: Perform backup of all entities
        var backupManifest = await PerformFullBackup();

        // Step 4: Verify backup files exist and are valid
        await VerifyBackupFiles(backupManifest);

        // Step 5: Clear databases
        await ClearDatabases();

        // Step 6: Verify databases are empty
        await VerifyDatabasesEmpty();

        // Step 7: Perform restore
        await PerformFullRestore(backupManifest.Name);

        // Step 8: Verify restored data matches original
        await VerifyRestoredData();

        _output.WriteLine("Full-stack backup/restore test completed successfully!");
    }

    private async Task CreateAndSaveTestData()
    {
        _output.WriteLine("Creating and saving test data...");

        // Create test users (SQLite)
        for (int i = 1; i <= 10; i++)
        {
            var user = new TestEntityUser
            {
                Name = $"User {i}",
                Email = $"user{i}@example.com",
                Age = 20 + (i % 50),
                IsActive = i % 2 == 0,
                CreatedAt = DateTime.UtcNow.AddDays(-i),
                Metadata = new Dictionary<string, object>
                {
                    { "Department", i % 3 == 0 ? "Engineering" : "Sales" },
                    { "Level", i % 5 + 1 }
                }
            };
            _originalUsers.Add(user);

            var savedUser = await Data<TestEntityUser, Guid>.UpsertAsync(user);
            _output.WriteLine($"   Saved user: {savedUser.Name} (ID: {savedUser.Id}) to SQLite");
        }

        // Create test products (MongoDB)
        for (int i = 1; i <= 5; i++)
        {
            var product = new TestEntityProduct
            {
                Name = $"Product {i}",
                Price = (decimal)(99.99 + (i * 50)),
                Category = i % 2 == 0 ? "Electronics" : "Books",
                InStock = true,
                Quantity = 100 + (i * 10),
                CreatedAt = DateTime.UtcNow.AddDays(-i * 2)
            };
            _originalProducts.Add(product);

            var savedProduct = await Data<TestEntityProduct, string>.UpsertAsync(product);
            _output.WriteLine($"   Saved product: {savedProduct.Name} (ID: {savedProduct.Id}) to MongoDB");
        }


        _output.WriteLine($"Created {_originalUsers.Count} users, {_originalProducts.Count} products");
    }

    private async Task VerifyDataInDatabases()
    {
        _output.WriteLine("Verifying data was saved to databases...");

        // Verify users in SQLite
        var savedUsers = await Data<TestEntityUser, Guid>.All();
        savedUsers.Should().HaveCount(_originalUsers.Count, "All users should be saved to SQLite");
        _output.WriteLine($"   SQLite: {savedUsers.Count} users found");

        // Verify products in MongoDB
        var savedProducts = await Data<TestEntityProduct, string>.All();
        savedProducts.Should().HaveCount(_originalProducts.Count, "All products should be saved to MongoDB");
        _output.WriteLine($"   MongoDB: {savedProducts.Count} products found");

    }

    private async Task<BackupManifest> PerformFullBackup()
    {
        _output.WriteLine("Performing full backup...");

        var backupService = _host!.Services.GetRequiredService<IBackupService>();
        var backupName = $"fullstack-test-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        var options = new GlobalBackupOptions
        {
            Description = "Full-stack test backup with real databases",
            Tags = new[] { "fullstack", "test", "multi-db" },
            StorageProfile = "test",
            MaxConcurrency = 3,
            BatchSize = 25
        };

        var manifest = await backupService.BackupAllEntitiesAsync(backupName, options);

        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().HaveCountGreaterThanOrEqualTo(2, "Should backup at least 2 entity types");

        _output.WriteLine($"Backup completed:");
        _output.WriteLine($"   Name: {manifest.Name}");
        _output.WriteLine($"   Entities: {manifest.Entities.Count}");
        _output.WriteLine($"   Total items: {manifest.Verification.TotalItemCount}");
        _output.WriteLine($"   Size: {manifest.Verification.TotalSizeBytes:N0} bytes");

        foreach (var entity in manifest.Entities)
        {
            _output.WriteLine($"   {entity.EntityType}: {entity.ItemCount} items, {entity.Provider} provider");
        }

        return manifest;
    }

    private async Task VerifyBackupFiles(BackupManifest manifest)
    {
        _output.WriteLine("Verifying backup files...");

        var backupPath = Path.Combine(_tempDirectory, "backups", $"{manifest.Name}-{manifest.CreatedAt:yyyyMMdd-HHmmss}.zip");
        File.Exists(backupPath).Should().BeTrue($"Backup file should exist at {backupPath}");

        var fileInfo = new FileInfo(backupPath);
        _output.WriteLine($"   Backup file: {fileInfo.Length:N0} bytes");

        // Verify ZIP structure
        using var archive = ZipFile.OpenRead(backupPath);

        // Check manifest
        var manifestEntry = archive.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull("Manifest should exist in backup");

        // Check entity files
        var expectedEntityFiles = manifest.Entities.Select(e => $"entities/{e.EntityType}.jsonl").ToList();
        foreach (var expectedFile in expectedEntityFiles)
        {
            var entry = archive.GetEntry(expectedFile);
            entry.Should().NotBeNull($"Entity file {expectedFile} should exist in backup");
            _output.WriteLine($"   {expectedFile}: {entry!.Length:N0} bytes");
        }

        // Check verification files
        archive.GetEntry("verification/checksums.json").Should().NotBeNull("Checksums file should exist");
        archive.GetEntry("verification/schema-snapshots.json").Should().NotBeNull("Schema snapshots should exist");

        _output.WriteLine("All backup files verified");
    }

    private async Task ClearDatabases()
    {
        _output.WriteLine("Clearing databases...");

        // Clear all entities from all databases
        await Data<TestEntityUser, Guid>.DeleteAllAsync();
        await Data<TestEntityProduct, string>.DeleteAllAsync();

        _output.WriteLine("All databases cleared");
    }

    private async Task VerifyDatabasesEmpty()
    {
        _output.WriteLine("Verifying databases are empty...");

        var users = await Data<TestEntityUser, Guid>.All();
        var products = await Data<TestEntityProduct, string>.All();
        users.Should().BeEmpty("SQLite should be empty");
        products.Should().BeEmpty("MongoDB should be empty");

        _output.WriteLine("All databases confirmed empty");
    }

    private async Task PerformFullRestore(string backupName)
    {
        _output.WriteLine("Performing full restore...");

        var restoreService = _host!.Services.GetRequiredService<IRestoreService>();

        var options = new GlobalRestoreOptions
        {
            ValidateBeforeRestore = true,
            ReplaceExisting = true,
            MaxConcurrency = 2,
            BatchSize = 25,
            StorageProfile = "test"
        };

        await restoreService.RestoreAllEntitiesAsync(backupName, options);

        _output.WriteLine("Restore completed");
    }

    private async Task VerifyRestoredData()
    {
        _output.WriteLine("Verifying restored data...");

        // Verify users restored to SQLite
        var restoredUsers = await Data<TestEntityUser, Guid>.All();
        restoredUsers.Should().HaveCount(_originalUsers.Count, "All users should be restored to SQLite");

        foreach (var originalUser in _originalUsers)
        {
            var restoredUser = restoredUsers.FirstOrDefault(u => u.Id == originalUser.Id);
            restoredUser.Should().NotBeNull($"User {originalUser.Id} should be restored");
            restoredUser!.Name.Should().Be(originalUser.Name);
            restoredUser.Email.Should().Be(originalUser.Email);
            restoredUser.Age.Should().Be(originalUser.Age);
            restoredUser.IsActive.Should().Be(originalUser.IsActive);
        }
        _output.WriteLine($"   SQLite: {restoredUsers.Count}/{_originalUsers.Count} users restored correctly");

        // Verify products restored to MongoDB
        var restoredProducts = await Data<TestEntityProduct, string>.All();
        restoredProducts.Should().HaveCount(_originalProducts.Count, "All products should be restored to MongoDB");

        foreach (var originalProduct in _originalProducts)
        {
            var restoredProduct = restoredProducts.FirstOrDefault(p => p.Id == originalProduct.Id);
            restoredProduct.Should().NotBeNull($"Product {originalProduct.Id} should be restored");
            restoredProduct!.Name.Should().Be(originalProduct.Name);
            restoredProduct.Price.Should().Be(originalProduct.Price);
            restoredProduct.Category.Should().Be(originalProduct.Category);
        }
        _output.WriteLine($"   MongoDB: {restoredProducts.Count}/{_originalProducts.Count} products restored correctly");


        _output.WriteLine("All data verified - backup/restore cycle successful!");
    }

    private async Task WarmupDataLayer()
    {
        _output.WriteLine("Warming up data layer...");

        // Ensure entity configurations are loaded
        var discoveryService = _host!.Services.GetRequiredService<IEntityDiscoveryService>();
        await discoveryService.WarmupAllEntitiesAsync();

        _output.WriteLine("Data layer warmed up");
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _mongoContainer.DisposeAsync();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

// Test entities that use different database providers
[DataAdapter("sqlite")] // SQLite provider
public class TestEntityUser : IEntity<Guid>
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

[DataAdapter("mongo")] // MongoDB provider
public class TestEntityProduct : IEntity<string>
{
    public string Id { get; set; } = Guid.CreateVersion7().ToString();
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public string Category { get; set; } = default!;
    public bool InStock { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


// File-based storage service for testing
public class FileBasedStorageService : Koan.Storage.Abstractions.IStorageService
{
    private readonly string _basePath;
    private readonly ILogger<FileBasedStorageService> _logger;

    public FileBasedStorageService(string basePath, ILogger<FileBasedStorageService> logger)
    {
        _basePath = basePath;
        _logger = logger;
    }

    public async Task<Koan.Storage.StorageObject> PutAsync(
        string storageProfile, string container, string key, Stream content,
        string? contentType, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        content.Position = 0;
        await content.CopyToAsync(fileStream, ct);

        return new Koan.Storage.StorageObject
        {
            Id = Guid.CreateVersion7().ToString(),
            Key = key,
            Size = content.Length,
            ContentType = contentType ?? "application/octet-stream",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<Stream> ReadAsync(string storageProfile, string container, string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, container, key);
        if (File.Exists(fullPath))
        {
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        }
        throw new FileNotFoundException($"File not found: {fullPath}");
    }

    public async IAsyncEnumerable<Koan.Storage.Abstractions.StorageObjectInfo> ListObjectsAsync(
        string profile, string container, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var containerPath = Path.Combine(_basePath, container);
        if (!Directory.Exists(containerPath))
            yield break;

        var files = Directory.GetFiles(containerPath, "*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(containerPath, filePath).Replace('\\', '/');

            // Apply prefix filter if specified
            if (!string.IsNullOrEmpty(prefix) && !relativePath.StartsWith(prefix))
                continue;

            var fileInfo = new FileInfo(filePath);
            yield return new Koan.Storage.Abstractions.StorageObjectInfo(
                relativePath,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc);
        }
    }

    public Task<bool> DeleteAsync(string storageProfile, string container, string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, container, key);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<(Stream Stream, long? Length)> ReadRangeAsync(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        throw new NotSupportedException("ReadRangeAsync not implemented in FileBasedStorageService");
    }

    public Task<bool> ExistsAsync(string profile, string container, string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, container, key);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<Koan.Storage.Abstractions.ObjectStat?> HeadAsync(string profile, string container, string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, container, key);
        if (!File.Exists(fullPath))
            return Task.FromResult<Koan.Storage.Abstractions.ObjectStat?>(null);

        var fileInfo = new FileInfo(fullPath);
        var stat = new Koan.Storage.Abstractions.ObjectStat(
            fileInfo.Length,
            "application/octet-stream",
            fileInfo.LastWriteTimeUtc,
            null);
        return Task.FromResult<Koan.Storage.Abstractions.ObjectStat?>(stat);
    }

    public Task<Koan.Storage.StorageObject> TransferToProfileAsync(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
    {
        throw new NotSupportedException("TransferToProfileAsync not implemented in FileBasedStorageService");
    }

    public Task<Uri> PresignReadAsync(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default)
    {
        throw new NotSupportedException("PresignReadAsync not implemented in FileBasedStorageService");
    }

    public Task<Uri> PresignWriteAsync(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
        throw new NotSupportedException("PresignWriteAsync not implemented in FileBasedStorageService");
    }

}
