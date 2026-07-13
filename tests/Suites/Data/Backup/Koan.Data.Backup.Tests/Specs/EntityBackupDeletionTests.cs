namespace Koan.Data.Backup.Tests.Specs;

public sealed class EntityBackupDeletionTests
{
    private const string ExpectedMessage =
        "Backup deletion is not supported. No backup was deleted. Keep the backup in place until a verified backup-management operation is available.";

    [Fact]
    public async Task DeleteBackup_returns_a_faulted_task_and_never_reports_success()
    {
        var document = new BackupDocument();
        Task<bool>? deletion = null;

        Action start = () =>
        {
            deletion = document.DeleteBackup("nightly-documents");
        };

        start.Should().NotThrow();
        deletion.Should().NotBeNull();
        deletion!.IsFaulted.Should().BeTrue();

        Func<Task<bool>> awaitDeletion = () => deletion!;
        var failure = await awaitDeletion.Should().ThrowAsync<NotSupportedException>();

        failure.Which.Message.Should().Be(ExpectedMessage);
    }

    private sealed class BackupDocument : Entity<BackupDocument>;
}
