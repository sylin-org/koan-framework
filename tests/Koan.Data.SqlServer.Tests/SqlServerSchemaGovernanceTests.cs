using Koan.Data.Relational.Tests;

namespace Koan.Data.SqlServer.Tests;

public class SqlServerSchemaGovernanceTests : RelationalSchemaGovernanceSharedTests<SqlServerAutoFixture, SqlServerSchemaGovernanceTests.Doc, string>, Xunit.IClassFixture<SqlServerAutoFixture>
{
    public SqlServerSchemaGovernanceTests(SqlServerAutoFixture fx) : base(fx) { }

    public class Doc : Abstractions.IEntity<string>
    {
        public Doc() { }
        public string Id { get; set; } = string.Empty;
        public string? Title { get; set; }
    }
}
