using Koan.Data.Core;
using Koan.Data.Connector.Sqlite;
using Koan.TestPipeline;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

public class SqliteConnectorSpec : IClassFixture<SqliteConnectorFixture>
{
    private readonly SqliteConnectorFixture _fixture;

    public SqliteConnectorSpec(SqliteConnectorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Sqlite: Basic CRUD operations work")]
    public async Task BasicCrud_Works()
    {
        // Arrange
        var entity = new TestEntity { Name = "SqliteTest" };

        // Act
        var saved = await entity.Save();
        var loaded = await TestEntity.Get(saved.Id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("SqliteTest");
    }

    // Add more tests for batch, health, and capabilities as needed
}
