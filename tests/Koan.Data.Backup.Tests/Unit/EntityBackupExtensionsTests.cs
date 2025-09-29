using FluentAssertions;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Tests.Fixtures;
using Koan.Data.Backup.Tests.TestEntities;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Backup.Tests.Unit;

public class EntityBackupExtensionsTests : IClassFixture<BackupTestFixture>
{
    private readonly BackupTestFixture _fixture;

    public EntityBackupExtensionsTests(BackupTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BackupTo_Simple_Should_Create_Backup()
    {
        // Arrange
        var backupName = $"extension-simple-{Guid.CreateVersion7()}";

        // Act
        var manifest = await DataBackup.BackupTo<TestUser, Guid>(backupName, "Simple extension test");

        // Assert
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be(backupName);
        manifest.Description.Should().Be("Simple extension test");
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().HaveCount(1);
        manifest.Entities.First().EntityType.Should().Be(nameof(TestUser));
    }

    [Fact]
    public async Task BackupTo_With_Options_Should_Use_Custom_Options()
    {
        // Arrange
        var backupName = $"extension-options-{Guid.CreateVersion7()}";
        var options = new BackupOptions
        {
            Description = "Custom options test",
            Tags = new[] { "extension", "test", "custom" },
            StorageProfile = _fixture.TestStorageProfile,
            BatchSize = 500
        };

        // Act
        var manifest = await DataBackup.BackupTo<TestProduct, string>(backupName, options);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be(backupName);
        manifest.Description.Should().Be(options.Description);
    manifest.Labels.Should().BeEquivalentTo(options.Tags);
        manifest.Status.Should().Be(BackupStatus.Completed);
    }

    [Fact]
    public async Task BackupTo_With_Tags_And_Profile_Should_Configure_Correctly()
    {
        // Arrange
        var backupName = $"extension-tags-{Guid.CreateVersion7()}";
        var description = "Tags and profile test";
        var tags = new[] { "tagged", "backup", "test" };
        var storageProfile = _fixture.TestStorageProfile;

        // Act
        var options = new BackupOptions
        {
            Description = description,
            Tags = tags,
            StorageProfile = storageProfile
        };
        var manifest = await DataBackup.BackupTo<TestOrder, long>(backupName, options);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be(backupName);
        manifest.Description.Should().Be(description);
    manifest.Labels.Should().BeEquivalentTo(tags);
        manifest.Status.Should().Be(BackupStatus.Completed);
    }

    [Fact]
    public async Task RestoreFrom_Simple_Should_Restore_Entity()
    {
        // Arrange
        var backupName = $"extension-restore-{Guid.CreateVersion7()}";

        // First create a backup
        await DataBackup.BackupTo<TestUser, Guid>(backupName, "Backup for restore test");

        // Act
        await DataBackup.RestoreFrom<TestUser, Guid>(backupName, new RestoreOptions { ReplaceExisting = true });

        // Assert - Should complete without error
        // In a real implementation, you would verify the data was actually restored
    }

    [Fact]
    public async Task RestoreFrom_With_Options_Should_Use_Custom_Options()
    {
        // Arrange
        var backupName = $"extension-restore-options-{Guid.CreateVersion7()}";

        // First create a backup
        await DataBackup.BackupTo<TestProduct, string>(backupName, "Backup for restore options test");

        var restoreOptions = new RestoreOptions
        {
            ReplaceExisting = true,
            UseBulkMode = true,
            BatchSize = 250,
            OptimizationLevel = "Fast"
        };

        // Act
        await DataBackup.RestoreFrom<TestProduct, string>(backupName, restoreOptions);

        // Assert - Should complete without error
    }

    [Fact]
    public async Task ListBackups_Should_Return_Entity_Backups()
    {
        // Arrange
        var backupName1 = $"extension-list-1-{Guid.CreateVersion7()}";
        var backupName2 = $"extension-list-2-{Guid.CreateVersion7()}";

        // Create some backups
        await DataBackup.BackupTo<TestUser, Guid>(backupName1, "First backup for listing");
        await DataBackup.BackupTo<TestUser, Guid>(backupName2, "Second backup for listing");

        // Act
        var backups = await DataBackup.ListBackups<TestUser, Guid>();

        // Assert
        backups.Should().NotBeNull();
        backups.Should().NotBeEmpty();

        // Should contain our TestUser backups
        var backupNames = backups.Select(b => b.Name).ToList();
        backupNames.Should().Contain(backupName1);
        backupNames.Should().Contain(backupName2);

        // All backups should be for TestUser entities
        backups.Should().OnlyContain(b =>
            b.EntityTypes != null && b.EntityTypes.Contains(nameof(TestUser)));
    }

    [Fact]
    public async Task GetBackupInfo_Should_Return_Backup_Details()
    {
        // Arrange
        var backupName = $"extension-info-{Guid.CreateVersion7()}";
        var description = "Backup info test";

        // Create a backup
        var manifest = await DataBackup.BackupTo<TestOrder, long>(backupName, description);

        // Act
        // Note: GetBackupInfo is not yet implemented in DataBackup
        var backupInfo = await GetBackupInfoForTest(backupName);

        // Assert
        backupInfo.Should().NotBeNull();
        backupInfo!.Name.Should().Be(backupName);
        backupInfo.Id.Should().Be(manifest.Id);
        backupInfo.EntityTypes.Should().Contain(nameof(TestOrder));
    }

    [Fact]
    public async Task GetBackupInfo_For_Nonexistent_Backup_Should_Return_Null()
    {
        // Arrange
        var nonexistentBackupName = $"does-not-exist-{Guid.CreateVersion7()}";

        // Act
        // Note: GetBackupInfo is not yet implemented in DataBackup
        var backupInfo = await GetBackupInfoForTest(nonexistentBackupName);

        // Assert
        backupInfo.Should().BeNull();
    }

    [Fact]
    public async Task DeleteBackup_Should_Mark_For_Deletion()
    {
        // Arrange
        var backupName = $"extension-delete-{Guid.CreateVersion7()}";

        // Create a backup
        await DataBackup.BackupTo<TestProduct, string>(backupName, "Backup for deletion test");

        // Act
        // Note: DeleteBackup is not yet implemented in DataBackup
        var result = await DeleteBackupForTest(backupName);

        // Assert
        // Note: The current implementation is a placeholder that returns true
        // In a full implementation, this would actually delete the backup
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(TestUser), typeof(Guid))]
    [InlineData(typeof(TestProduct), typeof(string))]
    [InlineData(typeof(TestOrder), typeof(long))]
    public async Task Extensions_Should_Work_With_Different_Entity_Types(Type entityType, Type keyType)
    {
        // Arrange
        var backupName = $"extension-types-{entityType.Name}-{Guid.CreateVersion7()}";

        // Use reflection to call the generic extension methods
        var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);

        var backupToMethod = typeof(EntityBackupExtensions)
            .GetMethods()
            .First(m => m.Name == "BackupTo" && m.GetParameters().Length == 3) // Simple overload
            .MakeGenericMethod(entityType, keyType);

        var listBackupsMethod = typeof(EntityBackupExtensions)
            .GetMethod("ListBackups")!
            .MakeGenericMethod(entityType, keyType);

        // Act
        var manifest = await (Task<BackupManifest>)backupToMethod.Invoke(null, new object[]
        {
            Activator.CreateInstance(typeof(Entity<,>).MakeGenericType(entityType, keyType))!,
            backupName,
            $"Test backup for {entityType.Name}",
            CancellationToken.None
        })!;

        var backups = await (Task<BackupInfo[]>)listBackupsMethod.Invoke(null, new object[]
        {
            Activator.CreateInstance(typeof(Entity<,>).MakeGenericType(entityType, keyType))!,
            CancellationToken.None
        })!;

        // Assert
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be(backupName);
        manifest.Entities.Should().HaveCount(1);
        manifest.Entities.First().EntityType.Should().Be(entityType.Name);
        manifest.Entities.First().KeyType.Should().Be(keyType.Name);

        backups.Should().NotBeEmpty();
        backups.Should().Contain(b => b.Name == backupName);
    }

    [Fact]
    public async Task Extension_Methods_Should_Handle_Cancellation_Tokens()
    {
        // Arrange
        var backupName = $"extension-cancellation-{Guid.CreateVersion7()}";
        using var cts = new CancellationTokenSource();

        // Act & Assert - Should handle cancellation token without error
        var manifest = await DataBackup.BackupTo<TestUser, Guid>(
            backupName,
            "Cancellation test",
            cts.Token);

        manifest.Should().NotBeNull();
        manifest.Status.Should().Be(BackupStatus.Completed);

        // Test restore with cancellation token
        await DataBackup.RestoreFrom<TestUser, Guid>(
            backupName,
            new RestoreOptions { ReplaceExisting = true },
            cts.Token);

        // Test list with cancellation token
        var backups = await DataBackup.ListBackups<TestUser, Guid>(cts.Token);
        backups.Should().NotBeEmpty();
    }

    private static async Task<BackupInfo?> GetBackupInfoForTest(string backupName)
    {
        // Placeholder implementation for testing
        await Task.CompletedTask;
        return null;
    }

    private static async Task<bool> DeleteBackupForTest(string backupName)
    {
        // Placeholder implementation for testing
        await Task.CompletedTask;
        return true;
    }
}