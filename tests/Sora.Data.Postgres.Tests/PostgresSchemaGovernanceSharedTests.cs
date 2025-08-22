using Sora.Data.Relational.Tests;
using System;

namespace Sora.Data.Postgres.Tests;

public class PostgresSchemaGovernanceSharedTests : RelationalSchemaGovernanceSharedTests<PostgresAutoFixture, PostgresSchemaGovernanceSharedTests.Todo, string>, Xunit.IClassFixture<PostgresAutoFixture>
{
    public PostgresSchemaGovernanceSharedTests(PostgresAutoFixture fx) : base(fx) { }

    public class Todo : Abstractions.IEntity<string>
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        // Add more properties as needed for projection tests
    }
}
