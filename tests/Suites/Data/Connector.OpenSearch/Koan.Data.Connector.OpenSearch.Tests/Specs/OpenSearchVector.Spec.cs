using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Connector.OpenSearch.Tests.Support;
using Koan.Testing.Extensions;
using Koan.Testing.Pipeline;
using Xunit.Abstractions;

namespace Koan.Data.Connector.OpenSearch.Tests.Specs;

public sealed class OpenSearchVectorSpec
{
    private readonly ITestOutputHelper _output;

    public OpenSearchVectorSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "OpenSearch: vector save and kNN search roundtrip")]
    [Trait("Category", "Integration")]
    public async Task Save_with_vector_and_search_similar()
    {
        await TestPipeline.For<OpenSearchVectorSpec>(_output, nameof(Save_with_vector_and_search_similar))
            .RequireDocker()
            .UsingOpenSearchContainer()
            .Using<OpenSearchConnectorFixture>("fixture", static ctx => OpenSearchConnectorFixture.Create(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<OpenSearchConnectorFixture>("fixture");
                await fixture.ResetAsync<OsDocument>();

                fixture.BindHost();
                (await OsDocument.All()).Should().BeEmpty("reset should leave no persisted documents");
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<OpenSearchConnectorFixture>("fixture");
                fixture.BindHost();

                var alpha = new OsDocument { Title = "Semantic search techniques", Content = "Vector embeddings for natural language processing" };
                var beta = new OsDocument { Title = "OpenSearch kNN plugin", Content = "Approximate nearest neighbor search with NMSLIB and Faiss" };
                var gamma = new OsDocument { Title = "PostgreSQL indexing", Content = "B-tree and GIN indexes for relational data" };

                await VectorData<OsDocument>.SaveWithVector(alpha, new[] { 0.95f, 0.05f, 0.0f }, Metadata(alpha));
                await VectorData<OsDocument>.SaveWithVector(beta, new[] { 0.0f, 0.0f, 1.0f }, Metadata(beta));
                await VectorData<OsDocument>.SaveWithVector(gamma, new[] { 0.15f, 0.8f, 0.05f }, Metadata(gamma));

                var stored = await OsDocument.All();
                stored.Should().HaveCount(3);

                var results = await VectorData<OsDocument>.Search(new VectorQueryOptions(
                    Query: new[] { 1.0f, 0.0f, 0.0f },
                    TopK: 3));

                results.Matches.Should().HaveCount(3);

                var topMatch = await OsDocument.Get(results.Matches[0].Id);
                topMatch.Should().NotBeNull();
                topMatch!.Title.Should().Be(alpha.Title, "the closest vector should match the semantic search document");

                results.Matches[0].Score.Should().BeGreaterThanOrEqualTo(results.Matches[1].Score, "scores should be sorted by relevance");
            })
            .Run();

        static IReadOnlyDictionary<string, object> Metadata(OsDocument doc)
            => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["searchText"] = doc.Title,
                ["kind"] = "document"
            };
    }
}

internal sealed class OsDocument : Entity<OsDocument>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public float[] Embedding { get; set; } = [];
}
