using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Connector.Weaviate.IntegrationTests;

public sealed class TestEntity : IEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
}
