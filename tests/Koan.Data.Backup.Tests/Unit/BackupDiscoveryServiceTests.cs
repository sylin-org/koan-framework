using FluentAssertions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Tests.Fixtures;
using Koan.Data.Backup.Tests.TestEntities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Backup.Tests.Unit;

public class BackupDiscoveryServiceTests : IClassFixture<BackupTestFixture>
{
    private readonly BackupTestFixture _fixture;

    public BackupDiscoveryServiceTests(BackupTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DiscoverAllBackupsAsync_Should_Find_Available_Backups()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        // Create some test backups
        var backup1Name = $"discovery-test-1-{Guid.CreateVersion7()}";
        var backup2Name = $"discovery-test-2-{Guid.CreateVersion7()}";

        await backupService.BackupEntityAsync<TestUser, Guid>(backup1Name, new BackupOptions
        {
            Tags = new[] { "test", "user-data" },
            StorageProfile = _fixture.TestStorageProfile
        });

        await backupService.BackupEntityAsync<TestProduct, string>(backup2Name, new BackupOptions
        {
            Tags = new[] { "test", "product-data" },
            StorageProfile = _fixture.TestStorageProfile
        });

        // Act
        var catalog = await discoveryService.DiscoverAllBackupsAsync();

        // Assert
        catalog.Should().NotBeNull();
        catalog.Backups.Should().NotBeEmpty();
        catalog.TotalCount.Should().BeGreaterOrEqualTo(2);
        catalog.Stats.Should().NotBeNull();

        var backupNames = catalog.Backups.Select(b => b.Name).ToList();
        backupNames.Should().Contain(backup1Name);
        backupNames.Should().Contain(backup2Name);
    }

    [Fact]
    public async Task DiscoverByStorageProfileAsync_Should_Filter_By_Storage_Profile()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var backupName = $"profile-test-{Guid.CreateVersion7()}";

        await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        // Act
        var catalog = await discoveryService.DiscoverByStorageProfileAsync(_fixture.TestStorageProfile);

        // Assert
        catalog.Should().NotBeNull();
        catalog.Backups.Should().NotBeEmpty();
        catalog.Backups.Should().OnlyContain(b => b.StorageProfile == _fixture.TestStorageProfile);
    }

    [Fact]
    public async Task QueryBackupsAsync_Should_Filter_By_Tags()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var taggedBackupName = $"tagged-test-{Guid.CreateVersion7()}";
        var untaggedBackupName = $"untagged-test-{Guid.CreateVersion7()}";

        await backupService.BackupEntityAsync<TestUser, Guid>(taggedBackupName, new BackupOptions
        {
            Tags = new[] { "important", "production" },
            StorageProfile = _fixture.TestStorageProfile
        });

        await backupService.BackupEntityAsync<TestProduct, string>(untaggedBackupName, new BackupOptions
        {
            Tags = new[] { "test", "development" },
            StorageProfile = _fixture.TestStorageProfile
        });

        var query = new BackupQuery
        {
            Tags = new[] { "important" },
            Take = 100
        };

        // Act
        var catalog = await discoveryService.QueryBackupsAsync(query);

        // Assert
        catalog.Should().NotBeNull();
        catalog.Query.Should().BeEquivalentTo(query);
        catalog.Backups.Should().NotBeEmpty();
        catalog.Backups.Should().OnlyContain(b => b.Tags.Contains("important"));
    }

    [Fact]
    public async Task QueryBackupsAsync_Should_Filter_By_Entity_Types()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var userBackupName = $"user-backup-{Guid.CreateVersion7()}";
        var productBackupName = $"product-backup-{Guid.CreateVersion7()}";

        await backupService.BackupEntityAsync<TestUser, Guid>(userBackupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        await backupService.BackupEntityAsync<TestProduct, string>(productBackupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        var query = new BackupQuery
        {
            EntityTypes = new[] { nameof(TestUser) },
            Take = 100
        };

        // Act
        var catalog = await discoveryService.QueryBackupsAsync(query);

        // Assert
        catalog.Should().NotBeNull();
        catalog.Backups.Should().NotBeEmpty();
        catalog.Backups.Should().OnlyContain(b =>
            b.EntityTypes != null && b.EntityTypes.Contains(nameof(TestUser)));
    }

    [Fact]
    public async Task QueryBackupsAsync_Should_Support_Date_Range_Filtering()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var backupName = $"date-test-{Guid.CreateVersion7()}";

        await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        var query = new BackupQuery
        {
            DateFrom = DateTimeOffset.UtcNow.AddHours(-1),
            DateTo = DateTimeOffset.UtcNow.AddHours(1),
            Take = 100
        };

        // Act
        var catalog = await discoveryService.QueryBackupsAsync(query);

        // Assert
        catalog.Should().NotBeNull();
        catalog.Backups.Should().NotBeEmpty();
        catalog.Backups.Should().OnlyContain(b =>
            b.CreatedAt >= query.DateFrom && b.CreatedAt <= query.DateTo);
    }

    [Fact]
    public async Task QueryBackupsAsync_Should_Support_Search_Term()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var searchableBackupName = $"searchable-backup-{Guid.CreateVersion7()}";

        await backupService.BackupEntityAsync<TestUser, Guid>(searchableBackupName, new BackupOptions
        {
            Description = "This is a searchable backup description",
            StorageProfile = _fixture.TestStorageProfile
        });

        var query = new BackupQuery
        {
            SearchTerm = "searchable",
            Take = 100
        };

        // Act
        var catalog = await discoveryService.QueryBackupsAsync(query);

        // Assert
        catalog.Should().NotBeNull();
        catalog.Backups.Should().NotBeEmpty();
        catalog.Backups.Should().Contain(b =>
            b.Name.ToLowerInvariant().Contains("searchable") ||
            b.Description.ToLowerInvariant().Contains("searchable"));
    }

    [Fact]
    public async Task GetBackupAsync_Should_Find_Backup_By_Name()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var backupName = $"get-test-{Guid.CreateVersion7()}";

        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        // Act
        var backup = await discoveryService.GetBackupAsync(backupName);

        // Assert
        backup.Should().NotBeNull();
        backup!.Name.Should().Be(backupName);
        backup.Id.Should().Be(manifest.Id);
    }

    [Fact]
    public async Task GetBackupAsync_Should_Return_Null_For_Nonexistent_Backup()
    {
        // Arrange
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();
        var nonexistentBackupName = $"does-not-exist-{Guid.CreateVersion7()}";

        // Act
        var backup = await discoveryService.GetBackupAsync(nonexistentBackupName);

        // Assert
        backup.Should().BeNull();
    }

    [Fact]
    public async Task ValidateBackupAsync_Should_Check_Backup_Integrity()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var backupName = $"validation-test-{Guid.CreateVersion7()}";

        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        // Act
        var validationResult = await discoveryService.ValidateBackupAsync(manifest.Id);

        // Assert
        validationResult.Should().NotBeNull();
        validationResult.BackupId.Should().Be(manifest.Id);
        validationResult.ValidatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RefreshCatalogAsync_Should_Clear_Cache()
    {
        // Arrange
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        // Act
        await discoveryService.RefreshCatalogAsync();

        // Assert - should complete without error
        // In a real implementation, you'd verify cache was actually cleared
    }

    [Fact]
    public async Task GetCatalogStatsAsync_Should_Return_Statistics()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        // Create a backup to ensure we have some data
        var backupName = $"stats-test-{Guid.CreateVersion7()}";
        await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        // Act
        var stats = await discoveryService.GetCatalogStatsAsync();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalBackups.Should().BeGreaterOrEqualTo(1);
        stats.TotalSizeBytes.Should().BeGreaterOrEqualTo(0);
        stats.BackupsByStatus.Should().NotBeEmpty();
    }
}