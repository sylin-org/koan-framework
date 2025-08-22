using Sora.Data.Relational.Tests;

namespace Sora.Data.Sqlite.Tests;

public class SqliteSchemaGovernanceSharedTests : RelationalSchemaGovernanceSharedTests<SqliteAutoFixture, SqliteSchemaGovernanceSharedTests.Todo, string>, Xunit.IClassFixture<SqliteAutoFixture>
{
    public SqliteSchemaGovernanceSharedTests(SqliteAutoFixture fx) : base(fx) { }

    public class Todo : Abstractions.IEntity<string>
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        // Add more properties as needed for projection tests
    }
}
