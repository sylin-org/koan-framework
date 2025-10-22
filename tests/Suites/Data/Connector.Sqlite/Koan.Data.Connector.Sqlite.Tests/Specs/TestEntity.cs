using Koan.Data.Core;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

public class TestEntity : Entity<TestEntity>
{
    public string Name { get; set; } = string.Empty;
}
