using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Vector;
using Sora.Data.Vector.Abstractions;
using Xunit;

namespace Sora.Data.Weaviate.IntegrationTests;

public sealed class WeaviateFilterAndStatsTests : IClassFixture<WeaviateAutoFixture>
{
    private readonly bool _available;
    private readonly IVectorSearchRepository<TestEntity, string>? _repo;

    public WeaviateFilterAndStatsTests(WeaviateAutoFixture fx)
    {
        _available = fx.Available;
        if (!_available) return;

        var services = new ServiceCollection().AddSora();
        services.Configure<Sora.Data.Weaviate.WeaviateOptions>(o =>
        {
            o.Endpoint = "http://localhost:8085";
            o.DefaultTopK = 5;
            o.MaxTopK = 50;
            o.Dimension = 3;
        });
        var sp = services.StartSora();
        _repo = sp.GetRequiredService<IDataService>().TryGetVectorRepository<TestEntity, string>();
    }

    [SkippableFact]
    public async Task Filter_Equals_And_Operators_Work()
    {
        Skip.IfNot(_available, "Weaviate not available");
        var repo = _repo!;
        await repo.UpsertAsync("x1", new float[] { 0.1f, 0.2f, 0.3f }, new { color = "red", price = 10 });
        await repo.UpsertAsync("x2", new float[] { 0.1f, 0.2f, 0.31f }, new { color = "blue", price = 20 });

        var q = new float[] { 0.1f, 0.2f, 0.3f };
        // color eq red
        var f1 = new Dictionary<string, object?> { ["color"] = "red" };
        var r1 = await repo.SearchAsync(new VectorQueryOptions(q, TopK: 5, Filter: f1));
        Assert.All(r1.Matches, m => Assert.NotEqual("x2", m.Id));

        // price gt 15 using operator DSL
        var f2 = new
        {
            @operator = "And",
            operands = new object[]
            {
                new { path = new [] { "price" }, @operator = "gt", value = 15 }
            }
        };
        var r2 = await repo.SearchAsync(new VectorQueryOptions(q, TopK: 5, Filter: f2));
        Assert.Contains(r2.Matches, m => m.Id == "x2");
    }

    [SkippableFact]
    public async Task Index_Clear_And_Stats()
    {
        Skip.IfNot(_available, "Weaviate not available");
        var repo = _repo!;
        await repo.UpsertAsync("y1", new float[] { 0.2f, 0.1f, 0.0f }, new { tag = "t" });
        var before = await repo.SearchAsync(new VectorQueryOptions(new float[] { 0.2f, 0.1f, 0.0f }, TopK: 5));
        Assert.True(before.Matches.Count >= 1);

        // Stats should be >= 1
    var exec = (Sora.Data.Abstractions.Instructions.IInstructionExecutor<TestEntity>)repo;
    var count = await exec.ExecuteAsync<int>(new Sora.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexStats));
        Assert.True(count >= 1);

        // Clear (destructive) with explicit opt-in
    await exec.ExecuteAsync<object>(new Sora.Data.Abstractions.Instructions.Instruction(VectorInstructions.IndexClear, Options: new Dictionary<string, object?> { ["AllowDestructive"] = true }));
        var after = await repo.SearchAsync(new VectorQueryOptions(new float[] { 0.2f, 0.1f, 0.0f }, TopK: 5));
        Assert.True(after.Matches.Count == 0);
    }

    public sealed class TestEntity : IEntity<string>
    {
        public string Id { get; set; } = string.Empty;
    }
}
