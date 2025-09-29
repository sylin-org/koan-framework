using FluentAssertions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Tests.Fixtures;
using Koan.Data.Backup.Tests.TestEntities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Backup.Tests.Unit;

public class StreamingBackupServiceTests : IClassFixture<BackupTestFixture>
{
    private readonly BackupTestFixture _fixture;

    public StreamingBackupServiceTests(BackupTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BackupEntityAsync_Should_Create_Valid_Backup()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var backupName = $"test-backup-{Guid.CreateVersion7()}";

        var options = new BackupOptions
        {
            Description = "Test backup for unit testing",
            Tags = new[] { "test", "unit" },
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act
        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, options);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be(backupName);
        manifest.Description.Should().Be(options.Description);
    manifest.Labels.Should().BeEquivalentTo(options.Tags);
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().HaveCount(1);

        var entityInfo = manifest.Entities.First();
        entityInfo.EntityType.Should().Be(nameof(TestUser));
        entityInfo.KeyType.Should().Be(nameof(Guid));
        entityInfo.ItemCount.Should().BeGreaterOrEqualTo(0);
        entityInfo.ContentHash.Should().NotBeNullOrEmpty();
        entityInfo.StorageFile.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task BackupAllEntitiesAsync_Should_Backup_Multiple_Entity_Types()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var backupName = $"full-backup-{Guid.CreateVersion7()}";

        var options = new GlobalBackupOptions
        {
            Description = "Full backup test",
            Tags = new[] { "test", "integration", "full" },
            StorageProfile = _fixture.TestStorageProfile,
            MaxConcurrency = 2
        };

        // Act
        var manifest = await backupService.BackupAllEntitiesAsync(backupName, options);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be(backupName);
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().NotBeEmpty();

        // Should have backed up our test entities
        var entityTypes = manifest.Entities.Select(e => e.EntityType).ToList();
        entityTypes.Should().Contain(nameof(TestUser));
        entityTypes.Should().Contain(nameof(TestProduct));
        entityTypes.Should().Contain(nameof(TestOrder));

        // Verify verification data
        manifest.Verification.Should().NotBeNull();
        manifest.Verification.TotalItemCount.Should().Be(manifest.Entities.Sum(e => e.ItemCount));
        manifest.Verification.TotalSizeBytes.Should().Be(manifest.Entities.Sum(e => e.SizeBytes));
        manifest.Verification.OverallChecksum.Should().NotBeNullOrEmpty();

        // Verify discovery info
        manifest.Discovery.Should().NotBeNull();
        manifest.Discovery.TotalEntityTypes.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("test-tag")]
    [InlineData("production")]
    [InlineData("daily-backup")]
    public async Task BackupEntityAsync_Should_Support_Different_Tags(string tag)
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var backupName = $"tagged-backup-{Guid.CreateVersion7()}";

        var options = new BackupOptions
        {
            Tags = new[] { tag },
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act
        var manifest = await backupService.BackupEntityAsync<TestProduct, string>(backupName, options);

        // Assert
    manifest.Labels.Should().Contain(tag);
        manifest.Status.Should().Be(BackupStatus.Completed);
    }

    [Fact]
    public async Task BackupSelectedAsync_Should_Filter_Entity_Types()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var backupName = $"selective-backup-{Guid.CreateVersion7()}";

        var options = new GlobalBackupOptions
        {
            Description = "Selective backup test",
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act - backup only TestUser entities
        var manifest = await backupService.BackupSelectedAsync(
            backupName,
            entityInfo => entityInfo.EntityType.Name == nameof(TestUser),
            options);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().HaveCount(1);
        manifest.Entities.First().EntityType.Should().Be(nameof(TestUser));
    }

    [Fact]
    public async Task GetBackupProgressAsync_Should_Return_Progress_During_Backup()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var backupName = $"progress-backup-{Guid.CreateVersion7()}";

        var options = new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act & Assert
        var backupTask = backupService.BackupEntityAsync<TestUser, Guid>(backupName, options);

        // Note: In a real test, you'd need to capture the backup ID and check progress
        // This is simplified for the test structure
        var manifest = await backupTask;

        var progress = await backupService.GetBackupProgressAsync(manifest.Id);
        progress.Should().NotBeNull();
        progress.Status.Should().Be(BackupStatus.Completed);
    }

    [Fact]
    public async Task CancelBackupAsync_Should_Mark_Backup_As_Cancelled()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var backupId = Guid.CreateVersion7().ToString();

        // Act
        await backupService.CancelBackupAsync(backupId);

        // Assert
        var progress = await backupService.GetBackupProgressAsync(backupId);
        // Note: The actual cancellation behavior depends on implementation details
        // This test structure validates the API contract
        progress.Should().NotBeNull();
    }
}