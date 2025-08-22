using Sora.Data.Relational.Tests;

namespace Sora.Data.SqlServer.Tests;

public class SqlServerSchemaGovernanceTests : RelationalSchemaGovernanceSharedTests<SqlServerAutoFixture, SqlServerSchemaGovernanceTests.Doc, string>, Xunit.IClassFixture<SqlServerAutoFixture>
{
    public SqlServerSchemaGovernanceTests(SqlServerAutoFixture fx) : base(fx) { }

    public class Doc : Sora.Data.Abstractions.IEntity<string>
    {
        public Doc() { }
        public string Id { get; set; } = string.Empty;
        public string? Title { get; set; }
    }
}
