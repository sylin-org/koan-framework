using Koan.Data.Core;
using Koan.Data.Connector.Backup;
using Koan.TestPipeline;

namespace Koan.Data.Connector.Backup.Tests.Specs;

public class BackupConnectorSpec : IClassFixture<BackupConnectorFixture>
{
    private readonly BackupConnectorFixture _fixture;

    public BackupConnectorSpec(BackupConnectorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Backup: Export/Import roundtrip works (pending instruction harness)")]
    public async Task ExportImport_Roundtrip_Works()
    {
        // TODO: Implement when instruction harness is available
        // Assert.True(false, "Instruction harness not yet implemented");
    }
}
