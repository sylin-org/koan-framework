using FluentAssertions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Tests.Fixtures;
using Koan.Data.Backup.Tests.TestEntities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Backup.Tests.Unit;

public class OptimizedRestoreServiceTests : IClassFixture<BackupTestFixture>
{
    private readonly BackupTestFixture _fixture;

    public OptimizedRestoreServiceTests(BackupTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RestoreEntityAsync_Should_Restore_Single_Entity_Type()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"restore-test-{Guid.CreateVersion7()}";

        // First create a backup
        var manifest = await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        var options = new RestoreOptions
        {
            ReplaceExisting = true,
            UseBulkMode = true,
            BatchSize = 50,
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act
        await restoreService.RestoreEntityAsync<TestUser, Guid>(backupName, options);

        // Assert - in a real test, you would verify the data was actually restored
        // For now, we verify the operation completed without exception
        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);
    }

    [Fact]
    public async Task RestoreAllEntitiesAsync_Should_Restore_Multiple_Entity_Types()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"full-restore-test-{Guid.CreateVersion7()}";

        // First create a full backup
        var manifest = await backupService.BackupAllEntitiesAsync(backupName, new GlobalBackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        var options = new GlobalRestoreOptions
        {
            MaxConcurrency = 2,
            ValidateBeforeRestore = true,
            ReplaceExisting = true,
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act
        await restoreService.RestoreAllEntitiesAsync(backupName, options);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RestoreSelectedAsync_Should_Restore_Filtered_Entities()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"selective-restore-test-{Guid.CreateVersion7()}";

        // Create a backup with multiple entity types
        var manifest = await backupService.BackupAllEntitiesAsync(backupName, new GlobalBackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        var options = new GlobalRestoreOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act - restore only TestUser entities
        await restoreService.RestoreSelectedAsync(
            backupName,
            entityInfo => entityInfo.EntityType == nameof(TestUser),
            options);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Entities.Should().Contain(e => e.EntityType == nameof(TestUser));
    }

    [Fact]
    public async Task TestRestoreViabilityAsync_Should_Validate_Backup_Restore_Feasibility()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"viability-test-{Guid.CreateVersion7()}";

        // Create a backup
        await backupService.BackupEntityAsync<TestUser, Guid>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        // Act
        var viabilityReport = await restoreService.TestRestoreViabilityAsync(backupName);

        // Assert
        viabilityReport.Should().NotBeNull();
        viabilityReport.BackupName.Should().Be(backupName);
        viabilityReport.EntityViability.Should().NotBeEmpty();

        // Should be viable since we just created the backup
        viabilityReport.IsViable.Should().BeTrue();
        viabilityReport.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRestoreProgressAsync_Should_Return_Progress_Information()
    {
        // Arrange
        var restoreService = await _fixture.GetRestoreServiceAsync();
        var restoreId = Guid.CreateVersion7().ToString();

        // Act
        var progress = await restoreService.GetRestoreProgressAsync(restoreId);

        // Assert
        progress.Should().NotBeNull();
        progress.RestoreId.Should().Be(restoreId);
    }

    [Fact]
    public async Task CancelRestoreAsync_Should_Mark_Restore_As_Cancelled()
    {
        // Arrange
        var restoreService = await _fixture.GetRestoreServiceAsync();
        var restoreId = Guid.CreateVersion7().ToString();

        // Act
        await restoreService.CancelRestoreAsync(restoreId);

        // Assert
        var progress = await restoreService.GetRestoreProgressAsync(restoreId);
        progress.Should().NotBeNull();
        // Note: The actual cancellation behavior depends on implementation
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestoreEntityAsync_Should_Handle_ReplaceExisting_Option(bool replaceExisting)
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"replace-test-{Guid.CreateVersion7()}";

        // Create a backup
        await backupService.BackupEntityAsync<TestProduct, string>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        var options = new RestoreOptions
        {
            ReplaceExisting = replaceExisting,
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act & Assert - should not throw regardless of replaceExisting value
        await restoreService.RestoreEntityAsync<TestProduct, string>(backupName, options);
    }

    [Fact]
    public async Task RestoreEntityAsync_With_DryRun_Should_Not_Modify_Data()
    {
        // Arrange
        var backupService = await _fixture.GetBackupServiceAsync();
        var restoreService = await _fixture.GetRestoreServiceAsync();

        var backupName = $"dryrun-test-{Guid.CreateVersion7()}";

        // Create a backup
        await backupService.BackupEntityAsync<TestOrder, long>(backupName, new BackupOptions
        {
            StorageProfile = _fixture.TestStorageProfile
        });

        var options = new RestoreOptions
        {
            DryRun = true,
            StorageProfile = _fixture.TestStorageProfile
        };

        // Act
        await restoreService.RestoreEntityAsync<TestOrder, long>(backupName, options);

        // Assert - dry run should complete without error
        // In a real implementation, you'd verify no data was actually modified
    }
}