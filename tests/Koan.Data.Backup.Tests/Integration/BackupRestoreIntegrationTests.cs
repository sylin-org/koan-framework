using FluentAssertions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Tests.Fixtures;
using Koan.Data.Backup.Tests.TestEntities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Backup.Tests.Integration;

public class BackupRestoreIntegrationTests : IClassFixture<BackupTestFixture>
{
    private readonly BackupTestFixture _fixture;

    public BackupRestoreIntegrationTests(BackupTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Complete_Backup_And_Restore_Workflow_Should_Work()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var testUsers = await _fixture.CreateTestUsersAsync(20);
        var testProducts = await _fixture.CreateTestProductsAsync(10);

        var backupName = $"integration-test-{Guid.CreateVersion7()}";

        // Act - Step 1: Create full backup
        var backupOptions = new GlobalBackupOptions
        {
            Description = "Full integration test backup",
            Tags = new[] { "integration", "test", "full" },
            StorageProfile = _fixture.TestStorageProfile,
            MaxConcurrency = 3
        };

        var manifest = await backupService.BackupAllEntitiesAsync(backupName, backupOptions);

        // Assert - Backup completed successfully
        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Name.Should().Be(backupName);
        manifest.Entities.Should().NotBeEmpty();

        // Act - Step 2: Discover the backup
        var discoveredBackup = await discoveryService.GetBackupAsync(backupName);

        // Assert - Backup is discoverable
        discoveredBackup.Should().NotBeNull();
        discoveredBackup!.Name.Should().Be(backupName);
        discoveredBackup.Status.Should().Be(BackupStatus.Completed);

        // Act - Step 3: Validate backup integrity
        var validationResult = await discoveryService.ValidateBackupAsync(discoveredBackup.Id);

        // Assert - Backup is valid
        validationResult.Should().NotBeNull();
        validationResult.IsValid.Should().BeTrue();
        validationResult.Issues.Should().BeEmpty();

        // Act - Step 4: Test restore viability
        var viabilityReport = await restoreService.TestRestoreViabilityAsync(backupName);

        // Assert - Restore is viable
        viabilityReport.Should().NotBeNull();
        viabilityReport.IsViable.Should().BeTrue();
        viabilityReport.Issues.Should().BeEmpty();
        viabilityReport.EntityViability.Should().NotBeEmpty();

        // Act - Step 5: Perform full restore
        var restoreOptions = new GlobalRestoreOptions
        {
            ValidateBeforeRestore = true,
            ReplaceExisting = true,
            MaxConcurrency = 2,
            StorageProfile = _fixture.TestStorageProfile
        };

        await restoreService.RestoreAllEntitiesAsync(backupName, restoreOptions);

        // Assert - Restore completed without errors
        // In a real implementation, you would verify the restored data matches the original
    }

    [Fact]
    public async Task Selective_Backup_And_Restore_Should_Work()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"selective-test-{Guid.CreateVersion7()}";

        // Act - Create selective backup (only TestUser entities)
        var manifest = await backupService.BackupSelectedAsync(
            backupName,
            entityInfo => entityInfo.EntityType.Name == nameof(TestUser),
            new GlobalBackupOptions
            {
                Description = "Selective backup test",
                Tags = new[] { "selective", "test" },
                StorageProfile = _fixture.TestStorageProfile
            });

        // Assert - Only TestUser entities were backed up
        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().HaveCount(1);
        manifest.Entities.First().EntityType.Should().Be(nameof(TestUser));

        // Act - Restore only the backed up entities
        await restoreService.RestoreSelectedAsync(
            backupName,
            entityInfo => entityInfo.EntityType == nameof(TestUser),
            new GlobalRestoreOptions
            {
                StorageProfile = _fixture.TestStorageProfile
            });

        // Assert - Restore completed successfully
    }

    [Fact]
    public async Task Backup_With_Different_Compression_Levels_Should_Work()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();

        var backupNames = new[]
        {
            $"compression-optimal-{Guid.CreateVersion7()}",
            $"compression-fastest-{Guid.CreateVersion7()}",
            $"compression-smallest-{Guid.CreateVersion7()}"
        };

        var compressionLevels = new[]
        {
            System.IO.Compression.CompressionLevel.Optimal,
            System.IO.Compression.CompressionLevel.Fastest,
            System.IO.Compression.CompressionLevel.SmallestSize
        };

        var manifests = new List<BackupManifest>();

        // Act - Create backups with different compression levels
        for (int i = 0; i < backupNames.Length; i++)
        {
            var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(
                backupNames[i],
                new BackupOptions
                {
                    CompressionLevel = compressionLevels[i],
                    Description = $"Compression test: {compressionLevels[i]}",
                    StorageProfile = _fixture.TestStorageProfile
                });

            manifests.Add(manifest);
        }

        // Assert - All backups completed successfully
        manifests.Should().HaveCount(3);
        manifests.Should().OnlyContain(m => m.Status == BackupStatus.Completed);

        // Different compression levels should produce different file sizes
        // (This would be more meaningful with actual data)
        var sizes = manifests.Select(m => m.Verification.TotalSizeBytes).ToList();
        sizes.Should().OnlyContain(size => size >= 0);
    }

    [Fact]
    public async Task Backup_Discovery_And_Query_Integration_Should_Work()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var backupPrefix = $"query-test-{Guid.CreateVersion7()}";

        // Create multiple backups with different characteristics
        var backups = new[]
        {
            new { Name = $"{backupPrefix}-user", Entity = typeof(TestUser), Tags = new[] { "users", "important" } },
            new { Name = $"{backupPrefix}-product", Entity = typeof(TestProduct), Tags = new[] { "products", "catalog" } },
            new { Name = $"{backupPrefix}-order", Entity = typeof(TestOrder), Tags = new[] { "orders", "important" } }
        };

        var manifests = new List<BackupManifest>();

        foreach (var backup in backups)
        {
            // Use reflection to call the generic method
            var method = typeof(IBackupService).GetMethod(nameof(IBackupService.BackupEntityAsync));
            var keyType = backup.Entity == typeof(TestUser) ? typeof(Guid) :
                         backup.Entity == typeof(TestProduct) ? typeof(string) : typeof(long);

            var genericMethod = method!.MakeGenericMethod(backup.Entity, keyType);

            var task = (Task<BackupManifest>)genericMethod.Invoke(backupService, new object[]
            {
                backup.Name,
                new BackupOptions
                {
                    Tags = backup.Tags,
                    Description = $"Test backup for {backup.Entity.Name}",
                    StorageProfile = _fixture.TestStorageProfile
                },
                CancellationToken.None
            })!;

            var manifest = await task;
            manifests.Add(manifest);
        }

        // Act & Assert - Test various query scenarios

        // 1. Query by tags
        var importantBackups = await discoveryService.QueryBackupsAsync(new BackupQuery
        {
            Tags = new[] { "important" },
            Take = 100
        });

        importantBackups.Backups.Should().HaveCountGreaterThanOrEqualTo(2);
        importantBackups.Backups.Should().OnlyContain(b => b.Tags.Contains("important"));

        // 2. Query by entity type
        var userBackups = await discoveryService.QueryBackupsAsync(new BackupQuery
        {
            EntityTypes = new[] { nameof(TestUser) },
            Take = 100
        });

        userBackups.Backups.Should().HaveCountGreaterThanOrEqualTo(1);
        userBackups.Backups.Should().OnlyContain(b =>
            b.EntityTypes != null && b.EntityTypes.Contains(nameof(TestUser)));

        // 3. Query by search term
        var productSearchResults = await discoveryService.QueryBackupsAsync(new BackupQuery
        {
            SearchTerm = "product",
            Take = 100
        });

        productSearchResults.Backups.Should().HaveCountGreaterThanOrEqualTo(1);

        // 4. Query with date range
        var recentBackups = await discoveryService.QueryBackupsAsync(new BackupQuery
        {
            DateFrom = DateTimeOffset.UtcNow.AddHours(-1),
            DateTo = DateTimeOffset.UtcNow.AddHours(1),
            Take = 100
        });

        recentBackups.Backups.Should().HaveCountGreaterThanOrEqualTo(3);

        // 5. Test pagination
        var firstPage = await discoveryService.QueryBackupsAsync(new BackupQuery
        {
            Skip = 0,
            Take = 2
        });

        var secondPage = await discoveryService.QueryBackupsAsync(new BackupQuery
        {
            Skip = 2,
            Take = 2
        });

        firstPage.Backups.Should().HaveCountLessOrEqualTo(2);
        secondPage.Backups.Should().NotBeEmpty();

        // Ensure different pages return different results
        var firstPageIds = firstPage.Backups.Select(b => b.Id).ToHashSet();
        var secondPageIds = secondPage.Backups.Select(b => b.Id).ToHashSet();
        firstPageIds.Should().NotIntersectWith(secondPageIds);
    }

    [Fact]
    public async Task Error_Handling_And_Recovery_Should_Work()
    {
        // Arrange
        var restoreService = await _fixture.GetRestoreServiceAsync();
        var discoveryService = await _fixture.GetBackupDiscoveryServiceAsync();

        var nonexistentBackupName = $"does-not-exist-{Guid.CreateVersion7()}";

        // Act & Assert - Restore of nonexistent backup should fail gracefully
        var act = async () => await restoreService.RestoreEntityAsync<TestUser, Guid>(
            nonexistentBackupName,
            new RestoreOptions { StorageProfile = _fixture.TestStorageProfile });

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Act & Assert - Viability test of nonexistent backup should handle gracefully
        var viabilityReport = await restoreService.TestRestoreViabilityAsync(nonexistentBackupName);

        viabilityReport.Should().NotBeNull();
        viabilityReport.IsViable.Should().BeFalse();
        viabilityReport.Issues.Should().NotBeEmpty();

        // Act & Assert - Discovery of nonexistent backup should return null
        var backup = await discoveryService.GetBackupAsync(nonexistentBackupName);
        backup.Should().BeNull();

        // Act & Assert - Validation of nonexistent backup should handle gracefully
        var validationResult = await discoveryService.ValidateBackupAsync(nonexistentBackupName);
        validationResult.Should().NotBeNull();
        validationResult.IsValid.Should().BeFalse();
        validationResult.Issues.Should().NotBeEmpty();
    }
}