using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Connector.ElasticSearch.Tests.Support;
using Koan.Testing.Extensions;
using Koan.Testing.Pipeline;
using Xunit.Abstractions;

namespace Koan.Data.Connector.ElasticSearch.Tests.Specs;

public sealed class ElasticSearchVectorSpec
{
    private readonly ITestOutputHelper _output;

    public ElasticSearchVectorSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "ElasticSearch: vector save and kNN search roundtrip")]
    [Trait("Category", "Integration")]
    public async Task Save_with_vector_and_search_similar()
    {
        await TestPipeline.For<ElasticSearchVectorSpec>(_output, nameof(Save_with_vector_and_search_similar))
            .RequireDocker()
            .UsingElasticSearchContainer()
            .Using<ElasticSearchConnectorFixture>("fixture", static ctx => ElasticSearchConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<ElasticSearchConnectorFixture>("fixture");
                await fixture.ResetAsync<EsDocument>();

                fixture.BindHost();
                (await EsDocument.All()).Should().BeEmpty("reset should leave no persisted documents");
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<ElasticSearchConnectorFixture>("fixture");
                fixture.BindHost();

                var alpha = new EsDocument { Title = "Machine learning fundamentals", Content = "Neural networks and deep learning overview" };
                var beta = new EsDocument { Title = "Elasticsearch kNN search", Content = "Approximate nearest neighbor search with HNSW" };
                var gamma = new EsDocument { Title = "Redis caching patterns", Content = "In-memory data structure store for caching" };

                await VectorData<EsDocument>.SaveWithVector(alpha, new[] { 0.95f, 0.05f, 0.0f }, Metadata(alpha));
                await VectorData<EsDocument>.SaveWithVector(beta, new[] { 0.0f, 0.0f, 1.0f }, Metadata(beta));
                await VectorData<EsDocument>.SaveWithVector(gamma, new[] { 0.15f, 0.8f, 0.05f }, Metadata(gamma));

                var stored = await EsDocument.All();
                stored.Should().HaveCount(3);

                var results = await VectorData<EsDocument>.Search(new VectorQueryOptions(
                    Query: new[] { 1.0f, 0.0f, 0.0f },
                    TopK: 3));

                results.Matches.Should().HaveCount(3);

                var topMatch = await EsDocument.Get(results.Matches[0].Id);
                topMatch.Should().NotBeNull();
                topMatch!.Title.Should().Be(alpha.Title, "the closest vector should match the ML document");

                results.Matches[0].Score.Should().BeGreaterThanOrEqualTo(results.Matches[1].Score, "scores should be sorted by relevance");
            })
            .Run();

        static IReadOnlyDictionary<string, object> Metadata(EsDocument doc)
            => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["searchText"] = doc.Title,
                ["kind"] = "document"
            };
    }
}

internal sealed class EsDocument : Entity<EsDocument>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public float[] Embedding { get; set; } = [];
}
