using Koan.TestPipeline;
using Koan.Data.Connector.Backup;

namespace Koan.Data.Connector.Backup.Tests.Support;

public class BackupConnectorFixture : TestPipelineFixture
{
    public BackupConnectorFixture()
        : base("backup", seedPack: null) // Add seed pack if available
    {
        // Additional setup if needed
    }
}
