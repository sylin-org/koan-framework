using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Testing.Vector;
using Xunit;

namespace Koan.Data.Vector.Connector.Weaviate.IntegrationTests;

public sealed class WeaviateVectorTests : VectorAcceptanceTests<TestEntity, string>, IClassFixture<WeaviateAutoFixture>
{
    private readonly WeaviateAutoFixture _fx;
    private IVectorSearchRepository<TestEntity, string>? _repo;

    public WeaviateVectorTests(WeaviateAutoFixture fx)
    {
        _fx = fx;
        if (!IsAvailable) return;

        var services = new ServiceCollection();
        services.AddKoan();
        services.AddKoanDataVector();
        // Configure Weaviate
        services.Configure<WeaviateOptions>(o =>
        {
            o.Endpoint = fx.BaseUrl ?? "http://localhost:8085";
            o.DefaultTopK = 5;
            o.MaxTopK = 50;
            o.Dimension = 5; // small dimension for tests
            o.Metric = "cosine";
        });
        var sp = services.StartKoan();
        var data = sp.GetRequiredService<IDataService>();
        _repo = data.TryGetVectorRepository<TestEntity, string>();
    }

    protected override bool IsAvailable => _fx.Available;
    protected override string SetName => "test";

    protected override IVectorSearchRepository<TestEntity, string> GetVectorRepo()
        => _repo ?? throw new SkipException("Weaviate not available");

    protected override IDataRepository<TestEntity, string>? GetPrimaryRepoOrNull() => null;

    protected override float[] Embed(TestEntity entity) => entity.Vector;
    protected override float[] EmbedQuery(string text) => new float[] { 0.1f, 0.2f, 0.05f, 0.33f, 0.8f };

    protected override string GetId(TestEntity entity) => entity.Id ?? string.Empty;
    protected override TestEntity WithId(TestEntity entity, string id) { entity.Id = id; return entity; }

    protected override IEnumerable<TestEntity> SeedEntities()
    {
        yield return new TestEntity { Id = "a", Vector = new float[] { 0.2f, 0.1f, 0.0f, 0.3f, 0.9f } };
        yield return new TestEntity { Id = "b", Vector = new float[] { 0.2f, 0.1f, 0.0f, 0.3f, 0.9f } };
        yield return new TestEntity { Id = "c", Vector = new float[] { 0.2f, 0.1f, 0.0f, 0.3f, 0.9f } };
    }

    // TestEntity moved to the namespace scope for use in the base type
}

