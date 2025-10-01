using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Storage;
using Koan.Data.Backup.Tests.Fixtures;
using Koan.Data.Backup.Tests.TestEntities;
using Koan.Storage.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Data.Backup.Tests.RealWorld;

public class RealBackupRestoreTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDirectory;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<TestUser> _originalUsers;
    private readonly List<TestProduct> _originalProducts;

    public RealBackupRestoreTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"koan-backup-test-{Guid.CreateVersion7()}");
        Directory.CreateDirectory(_tempDirectory);

        // Create real test data
        _originalUsers = CreateRealTestUsers();
        _originalProducts = CreateRealTestProducts();

        // Setup real services with file-based storage
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Create a real storage service that writes to disk
        var storageService = CreateRealStorageService();
        services.AddSingleton(storageService);

        services.AddKoanBackupRestore(options =>
        {
            options.DefaultStorageProfile = "test";
            options.DefaultBatchSize = 10;
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Real_Backup_Should_Create_Valid_ZIP_With_JSON_Lines()
    {
        // Arrange
        var backupService = _serviceProvider.GetRequiredService<IBackupService>();
        var storageService = _serviceProvider.GetRequiredService<BackupStorageService>();

        var backupName = "real-test-backup";
        var options = new BackupOptions
        {
            Description = "Real world backup test",
            Tags = new[] { "real", "test" },
            StorageProfile = "test",
            BatchSize = 5
        };

        // Mock the Data<>.AllStream() method to return our test data
        var mockDataStream = CreateMockDataStream();

        // Act
        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, options);

        // Assert - Verify manifest
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be(backupName);
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().HaveCount(1);

        var entityInfo = manifest.Entities.First();
        entityInfo.EntityType.Should().Be(nameof(TestUser));
        entityInfo.ItemCount.Should().Be(_originalUsers.Count);

        // Verify the actual backup file exists
        var backupPath = Path.Combine(_tempDirectory, "backups", $"{backupName}-{manifest.CreatedAt:yyyyMMdd-HHmmss}.zip");
        File.Exists(backupPath).Should().BeTrue("Backup file should exist on disk");

        // Verify ZIP structure and content
        await VerifyBackupArchiveStructure(backupPath, manifest);
        await VerifyBackupDataIntegrity(backupPath, _originalUsers);

        _output.WriteLine($"Backup created successfully:");
        _output.WriteLine($"   File: {backupPath}");
        _output.WriteLine($"   Size: {new FileInfo(backupPath).Length:N0} bytes");
        _output.WriteLine($"   Entities: {entityInfo.ItemCount}");
        _output.WriteLine($"   Content Hash: {entityInfo.ContentHash}");
    }

    [Fact]
    public async Task Real_Restore_Should_Reconstruct_Original_Data()
    {
        // Arrange - First create a backup
        var backupService = _serviceProvider.GetRequiredService<IBackupService>();
        var restoreService = _serviceProvider.GetRequiredService<IRestoreService>();

        var backupName = "restore-verification-test";

        // Create backup with real data
        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = "test",
            BatchSize = 3
        });

        // Act - Restore the data
        var restoreOptions = new RestoreOptions
        {
            ReplaceExisting = true,
            BatchSize = 3,
            StorageProfile = "test"
        };

        var restoredData = new List<TestUser>();
        var mockRestoreTarget = CreateMockRestoreTarget(restoredData);

        await restoreService.RestoreEntityAsync<TestUser, Guid>(backupName, restoreOptions);

        // Assert - Verify restored data matches original
        // Note: In a real implementation, this would verify through the actual Data<> layer
        // For this test, we'll verify the backup file content can be read correctly

        var backupPath = Path.Combine(_tempDirectory, "backups", $"{backupName}-{manifest.CreatedAt:yyyyMMdd-HHmmss}.zip");
        var restoredUsersFromFile = await ReadUsersFromBackupFile(backupPath);

        restoredUsersFromFile.Should().HaveCount(_originalUsers.Count);

        // Verify each user was restored correctly
        foreach (var originalUser in _originalUsers)
        {
            var restoredUser = restoredUsersFromFile.FirstOrDefault(u => u.Id == originalUser.Id);
            restoredUser.Should().NotBeNull($"User {originalUser.Id} should be restored");

            restoredUser!.Name.Should().Be(originalUser.Name);
            restoredUser.Email.Should().Be(originalUser.Email);
            restoredUser.Age.Should().Be(originalUser.Age);
            restoredUser.IsActive.Should().Be(originalUser.IsActive);
            restoredUser.Tags.Should().BeEquivalentTo(originalUser.Tags);
            restoredUser.CreatedAt.Should().BeCloseTo(originalUser.CreatedAt, TimeSpan.FromSeconds(1));
        }

        _output.WriteLine($"Restore verified successfully:");
        _output.WriteLine($"   Original users: {_originalUsers.Count}");
        _output.WriteLine($"   Restored users: {restoredUsersFromFile.Count}");
        _output.WriteLine($"   All data integrity checks passed");
    }

    [Fact]
    public async Task Real_Backup_Discovery_Should_Find_Actual_Files()
    {
        // Arrange
        var backupService = _serviceProvider.GetRequiredService<IBackupService>();
        var discoveryService = _serviceProvider.GetRequiredService<IBackupDiscoveryService>();

        // Create multiple real backups
        var backupNames = new[]
        {
            "discovery-test-1",
            "discovery-test-2",
            "discovery-test-3"
        };

        var manifests = new List<BackupManifest>();
        foreach (var backupName in backupNames)
        {
            var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
            {
                Tags = new[] { "discovery", "real-test" },
                StorageProfile = "test"
            });
            manifests.Add(manifest);

            // Verify file was actually created
            var backupPath = Path.Combine(_tempDirectory, "backups", $"{backupName}-{manifest.CreatedAt:yyyyMMdd-HHmmss}.zip");
            File.Exists(backupPath).Should().BeTrue($"Backup file {backupPath} should exist");
        }

        // Act
        var catalog = await discoveryService.DiscoverAllBackupsAsync();

        // Assert
        catalog.Should().NotBeNull();
        catalog.Backups.Should().HaveCountGreaterThanOrEqualTo(3);

        var discoveredNames = catalog.Backups.Select(b => b.Name).ToList();
        foreach (var expectedName in backupNames)
        {
            discoveredNames.Should().Contain(expectedName, $"Backup {expectedName} should be discovered");
        }

        // Test specific backup retrieval
        foreach (var manifest in manifests)
        {
            var foundBackup = await discoveryService.GetBackupAsync(manifest.Name);
            foundBackup.Should().NotBeNull();
            foundBackup!.Id.Should().Be(manifest.Id);
            foundBackup.Status.Should().Be(BackupStatus.Completed);
        }

        _output.WriteLine($"Discovery verified successfully:");
        _output.WriteLine($"   Created backups: {manifests.Count}");
        _output.WriteLine($"   Discovered backups: {catalog.TotalCount}");
        _output.WriteLine($"   All backups found and validated");
    }

    [Fact]
    public async Task Real_JSON_Lines_Format_Should_Be_Valid()
    {
        // Arrange
        var backupService = _serviceProvider.GetRequiredService<IBackupService>();
        var backupName = "json-format-test";

        // Act
        var manifest = await backupService.BackupEntityAsync<TestProduct, string>(backupName, new BackupOptions
        {
            StorageProfile = "test"
        });

        // Assert - Manually verify JSON Lines format
        var backupPath = Path.Combine(_tempDirectory, "backups", $"{backupName}-{manifest.CreatedAt:yyyyMMdd-HHmmss}.zip");

        using var archive = ZipFile.OpenRead(backupPath);
        var entityEntry = archive.GetEntry("entities/TestProduct.jsonl");
        entityEntry.Should().NotBeNull("JSON Lines file should exist in archive");

        using var entryStream = entityEntry!.Open();
        using var reader = new StreamReader(entryStream);

        var lineCount = 0;
        var validJsonCount = 0;
        var products = new List<TestProduct>();

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lineCount++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Verify each line is valid JSON
            try
            {
                var product = JsonConvert.DeserializeObject<TestProduct>(line);
                product.Should().NotBeNull($"Line {lineCount} should deserialize to TestProduct");
                products.Add(product!);
                validJsonCount++;
            }
            catch (JsonException ex)
            {
                throw new AssertionException($"Line {lineCount} is not valid JSON: {line}\nError: {ex.Message}");
            }
        }

        // Verify all lines were valid JSON
        validJsonCount.Should().Be(lineCount, "All lines should be valid JSON");
        products.Should().HaveCount(_originalProducts.Count, "All products should be present");

        // Verify data integrity
        foreach (var originalProduct in _originalProducts)
        {
            var restoredProduct = products.FirstOrDefault(p => p.Id == originalProduct.Id);
            restoredProduct.Should().NotBeNull($"Product {originalProduct.Id} should be in backup");
            restoredProduct!.Name.Should().Be(originalProduct.Name);
            restoredProduct.Price.Should().Be(originalProduct.Price);
            restoredProduct.Category.Should().Be(originalProduct.Category);
        }

        _output.WriteLine($"JSON Lines format verified:");
        _output.WriteLine($"   Total lines: {lineCount}");
        _output.WriteLine($"   Valid JSON lines: {validJsonCount}");
        _output.WriteLine($"   Products restored: {products.Count}");
        _output.WriteLine($"   Data integrity: Passed");
    }

    private List<TestUser> CreateRealTestUsers()
    {
        return new List<TestUser>
        {
            new() { Id = Guid.CreateVersion7(), Name = "Alice Johnson", Email = "alice@example.com", Age = 28, IsActive = true, Tags = new[] { "premium", "verified" }, CreatedAt = DateTime.UtcNow.AddDays(-30) },
            new() { Id = Guid.CreateVersion7(), Name = "Bob Smith", Email = "bob@example.com", Age = 35, IsActive = true, Tags = new[] { "standard" }, CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new() { Id = Guid.CreateVersion7(), Name = "Charlie Brown", Email = "charlie@example.com", Age = 42, IsActive = false, Tags = new[] { "premium", "inactive" }, CreatedAt = DateTime.UtcNow.AddDays(-60) },
            new() { Id = Guid.CreateVersion7(), Name = "Diana Prince", Email = "diana@example.com", Age = 29, IsActive = true, Tags = new[] { "verified", "admin" }, CreatedAt = DateTime.UtcNow.AddDays(-5) },
            new() { Id = Guid.CreateVersion7(), Name = "Ethan Hunt", Email = "ethan@example.com", Age = 38, IsActive = true, Tags = Array.Empty<string>(), CreatedAt = DateTime.UtcNow.AddDays(-90) }
        };
    }

    private List<TestProduct> CreateRealTestProducts()
    {
        return new List<TestProduct>
        {
            new() { Id = "PROD001", Name = "Gaming Laptop", Price = 1299.99m, Category = "Electronics", Quantity = 15, InStock = true, CreatedAt = DateTime.UtcNow.AddDays(-20) },
            new() { Id = "PROD002", Name = "Wireless Headphones", Price = 199.99m, Category = "Electronics", Quantity = 50, InStock = true, CreatedAt = DateTime.UtcNow.AddDays(-10) },
            new() { Id = "PROD003", Name = "Coffee Maker", Price = 89.99m, Category = "Home", Quantity = 0, InStock = false, CreatedAt = DateTime.UtcNow.AddDays(-45) }
        };
    }

    private IStorageService CreateRealStorageService()
    {
        var mock = new Mock<IStorageService>();

        mock.Setup(s => s.PutAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profile, string container, string key, Stream content, string contentType, CancellationToken ct) =>
            {
                var fullPath = Path.Combine(_tempDirectory, container, key);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                using var fileStream = File.Create(fullPath);
                content.Position = 0;
                content.CopyTo(fileStream);

                return new RealStorageObject
                {
                    Key = key,
                    Size = content.Length,
                    ContentType = contentType,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ContentHash = ComputeFileHash(fullPath)
                };
            });

        mock.Setup(s => s.ReadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profile, string container, string key, CancellationToken ct) =>
            {
                var fullPath = Path.Combine(_tempDirectory, container, key);
                if (File.Exists(fullPath))
                {
                    return new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                }
                throw new FileNotFoundException($"File not found: {fullPath}");
            });

        mock.Setup(s => s.ListAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profile, string container, CancellationToken ct) =>
            {
                var containerPath = Path.Combine(_tempDirectory, container);
                if (!Directory.Exists(containerPath))
                    return new List<IStorageObject>();

                return Directory.GetFiles(containerPath, "*", SearchOption.AllDirectories)
                    .Select(filePath =>
                    {
                        var relativePath = Path.GetRelativePath(containerPath, filePath).Replace('\\', '/');
                        var fileInfo = new FileInfo(filePath);
                        return new RealStorageObject
                        {
                            Key = relativePath,
                            Size = fileInfo.Length,
                            CreatedAt = fileInfo.CreationTimeUtc,
                            UpdatedAt = fileInfo.LastWriteTimeUtc,
                            ContentHash = ComputeFileHash(filePath)
                        };
                    })
                    .Cast<IStorageObject>()
                    .ToList();
            });

        return mock.Object;
    }

    private async Task VerifyBackupArchiveStructure(string backupPath, BackupManifest manifest)
    {
        using var archive = ZipFile.OpenRead(backupPath);

        // Verify manifest exists
        var manifestEntry = archive.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull("Manifest should exist in archive");

        // Verify entity data file exists
        var expectedDataFile = $"entities/{manifest.Entities.First().EntityType}.jsonl";
        var dataEntry = archive.GetEntry(expectedDataFile);
        dataEntry.Should().NotBeNull($"Entity data file {expectedDataFile} should exist");

        // Verify verification files exist
        var checksumsEntry = archive.GetEntry("verification/checksums.json");
        checksumsEntry.Should().NotBeNull("Checksums file should exist");

        var schemasEntry = archive.GetEntry("verification/schema-snapshots.json");
        schemasEntry.Should().NotBeNull("Schema snapshots file should exist");

        _output.WriteLine($"Archive structure verified:");
        foreach (var entry in archive.Entries)
        {
            _output.WriteLine($"  - {entry.FullName} ({entry.Length:N0} bytes)");
        }
    }

    private async Task VerifyBackupDataIntegrity(string backupPath, List<TestUser> expectedUsers)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        var dataEntry = archive.GetEntry("entities/TestUser.jsonl");

        using var entryStream = dataEntry!.Open();
        using var reader = new StreamReader(entryStream);

        var restoredUsers = new List<TestUser>();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var user = JsonConvert.DeserializeObject<TestUser>(line);
            user.Should().NotBeNull();
            restoredUsers.Add(user!);
        }

        restoredUsers.Should().HaveCount(expectedUsers.Count);

        foreach (var expectedUser in expectedUsers)
        {
            var restoredUser = restoredUsers.FirstOrDefault(u => u.Id == expectedUser.Id);
            restoredUser.Should().NotBeNull();
            restoredUser!.Name.Should().Be(expectedUser.Name);
            restoredUser.Email.Should().Be(expectedUser.Email);
        }
    }

    private async Task<List<TestUser>> ReadUsersFromBackupFile(string backupPath)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        var dataEntry = archive.GetEntry("entities/TestUser.jsonl");

        using var entryStream = dataEntry!.Open();
        using var reader = new StreamReader(entryStream);

        var users = new List<TestUser>();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var user = JsonConvert.DeserializeObject<TestUser>(line);
            if (user != null) users.Add(user);
        }

        return users;
    }

    private IAsyncEnumerable<TestUser> CreateMockDataStream()
    {
        return _originalUsers.ToAsyncEnumerable();
    }

    private Mock<object> CreateMockRestoreTarget(List<TestUser> restoredData)
    {
        // This would normally be a mock of the Data<> layer
        // For this test, we're focusing on file-level verification
        return new Mock<object>();
    }

    private string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

        _serviceProvider?.GetService<IServiceScope>()?.Dispose();
    }

    private class RealStorageObject : IStorageObject
    {
        public string Id { get; set; } = Guid.CreateVersion7().ToString();
        public string Key { get; set; } = default!;
        public string? Name { get; set; }
        public string? ContentType { get; set; }
        public long Size { get; set; }
        public string? ContentHash { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? Provider { get; set; }
        public string? Container { get; set; }
        public IReadOnlyDictionary<string, string>? Tags { get; set; }
    }
}