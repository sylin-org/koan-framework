using System.Text.Json.Nodes;
using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineSeedExtensions
{
    public static TestPipeline UsingSeedPack(this TestPipeline pipeline, string packId, string key = "seed", Action<TestContext, SeedPackFixture>? onReady = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);

        return pipeline.Using(key, ctx => ValueTask.FromResult(new SeedPackFixture(packId)), onReady);
    }

    public static SeedPackFixture GetSeedPack(this TestContext context, string key = "seed")
        => context.GetRequiredItem<SeedPackFixture>(key);

    public static JsonNode GetSeedPackDocument(this TestContext context, string key = "seed")
        => context.GetSeedPack(key).Document;
}