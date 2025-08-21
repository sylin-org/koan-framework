using Sora.Data.Relational.Tests;
using System;
using Xunit;

namespace Sora.Data.Postgres.Tests;

public class PostgresSchemaGovernanceSharedTests : RelationalSchemaGovernanceSharedTests<PostgresAutoFixture, PostgresSchemaGovernanceSharedTests.Todo, string>, Xunit.IClassFixture<PostgresAutoFixture>
{
    public PostgresSchemaGovernanceSharedTests(PostgresAutoFixture fx) : base(fx) { }

    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        // Add more properties as needed for projection tests
    }
}
