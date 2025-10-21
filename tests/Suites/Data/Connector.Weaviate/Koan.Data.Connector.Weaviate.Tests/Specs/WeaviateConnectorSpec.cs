using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Connector.Weaviate.Tests.Support;
using Koan.Testing.Extensions;
using Koan.Testing.Pipeline;
using Xunit.Abstractions;

namespace Koan.Data.Connector.Weaviate.Tests.Specs;

public sealed class WeaviateConnectorSpec
{
    private readonly ITestOutputHelper _output;

    public WeaviateConnectorSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "Weaviate: vector CRUD and semantic search roundtrip")]
    public async Task Vector_crud_and_search_roundtrip()
    {
        await TestPipeline.For<WeaviateConnectorSpec>(_output, nameof(Vector_crud_and_search_roundtrip))
            .RequireDocker()
            .UsingWeaviateContainer()
            .Using<WeaviateConnectorFixture>("fixture", static ctx => WeaviateConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<WeaviateConnectorFixture>("fixture");
                await fixture.ResetAsync<VectorNote>();

                fixture.BindHost();
                (await VectorNote.All()).Should().BeEmpty("reset should leave no persisted documents");

                var emptySearch = await VectorData<VectorNote>.SearchAsync(new VectorQueryOptions(new[] { 0f, 0f, 0f }, TopK: 3));
                emptySearch.Matches.Should().BeEmpty("reset should remove residual vectors");
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<WeaviateConnectorFixture>("fixture");
                fixture.BindHost();

                var repo = fixture.Vectors.TryGetRepository<VectorNote, string>();
                repo.Should().NotBeNull("vector repository should resolve for weaviate");

                var capabilities = repo as IVectorCapabilities;
                capabilities.Should().NotBeNull("weaviate adapter exposes capability metadata");
                capabilities!.Capabilities.Should().HaveFlag(VectorCapabilities.Hybrid);
                capabilities.Capabilities.Should().HaveFlag(VectorCapabilities.BulkUpsert);
                capabilities.Capabilities.Should().HaveFlag(VectorCapabilities.BulkDelete);

                var alpha = new VectorNote { Title = "Graph embeddings" };
                var beta = new VectorNote { Title = "Weaviate adapters" };
                var gamma = new VectorNote { Title = "Redis cache" };

                await VectorData<VectorNote>.SaveWithVector(alpha, new[] { 0.95f, 0.05f, 0.0f }, Metadata(alpha));
                await VectorData<VectorNote>.SaveWithVector(beta, new[] { 0.0f, 0.0f, 1.0f }, Metadata(beta));
                await VectorData<VectorNote>.SaveWithVector(gamma, new[] { 0.15f, 0.8f, 0.05f }, Metadata(gamma));

                var stored = await VectorNote.All();
                stored.Should().HaveCount(3);

                alpha.Title = "Graph embeddings updated";
                await VectorData<VectorNote>.SaveWithVector(alpha, new[] { 0.97f, 0.02f, 0.01f }, Metadata(alpha));

                var vectorOnly = await VectorData<VectorNote>.SearchAsync(new VectorQueryOptions(new[] { 1.0f, 0.0f, 0.0f }, TopK: 3));
                vectorOnly.Matches.Should().HaveCount(3);
                vectorOnly.ContinuationToken.Should().BeNull();

                var topMatch = await VectorNote.Get(vectorOnly.Matches[0].Id);
                topMatch.Should().NotBeNull();
                topMatch!.Title.Should().Be(alpha.Title);

                var betaVectorOnlyMatch = vectorOnly.Matches.Single(match => match.Id == beta.Id);
                var betaVectorOnlyRank = vectorOnly.Matches
                    .Select((match, index) => (match, index))
                    .First(tuple => tuple.match.Id == beta.Id)
                    .index;
                betaVectorOnlyRank.Should().BeGreaterThan(0, "vector-only scoring should not favor the text match");

                var hybrid = await VectorData<VectorNote>.SearchAsync(new VectorQueryOptions(
                    Query: new[] { 0.5f, 0.4f, 0.1f },
                    TopK: 3,
                    SearchText: "adapter",
                    Alpha: 0.35f));

                hybrid.Matches.Count.Should().BeGreaterThanOrEqualTo(2, "hybrid search returns multiple candidates");
                hybrid.Matches[0].Score.Should().BeGreaterThanOrEqualTo(hybrid.Matches[1].Score, "hybrid scores should reflect sorted relevance");

                var betaHybridMatch = hybrid.Matches.Single(match => match.Id == beta.Id);
                var betaHybridRank = hybrid.Matches
                    .Select((match, index) => (match, index))
                    .First(tuple => tuple.match.Id == beta.Id)
                    .index;
                betaHybridRank.Should().BeLessThanOrEqualTo(betaVectorOnlyRank, "keyword weighting should not demote the text-matching note");
                betaHybridMatch.Score.Should().BeGreaterThan(betaVectorOnlyMatch.Score, "hybrid scoring should boost the text match compared to pure vector results");

                var textOnly = await VectorData<VectorNote>.SearchAsync(new VectorQueryOptions(
                    Query: new[] { 0f, 0f, 0f },
                    TopK: 1,
                    SearchText: "weaviate adapters",
                    Alpha: 0.0));
                textOnly.Matches.Should().ContainSingle("text-only search should use persisted metadata");
                textOnly.Matches[0].Id.Should().Be(beta.Id);
            })
            .RunAsync();

        static IReadOnlyDictionary<string, object> Metadata(VectorNote note)
            => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["searchText"] = note.Title,
                ["kind"] = "note"
            };
    }
}

internal sealed class VectorNote : Entity<VectorNote>
{
    public string Title { get; set; } = string.Empty;
}
