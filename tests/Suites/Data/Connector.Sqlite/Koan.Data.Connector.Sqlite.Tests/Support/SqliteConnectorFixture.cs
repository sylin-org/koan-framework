using Koan.TestPipeline;
using Koan.Data.Connector.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Support;

public class SqliteConnectorFixture : TestPipelineFixture
{
    public SqliteConnectorFixture()
        : base("sqlite", seedPack: null) // Add seed pack if available
    {
        // Additional setup if needed
    }
}
