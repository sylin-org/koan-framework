using AwesomeAssertions;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Tests.Data.AI.Specs;

[Embedding(Template = "{Title}", Dimensions = 5)]
public sealed class Note : Entity<Note>
{
    public string Title { get; set; } = "";
}

/// <summary>Rollout item 3: the type-scoped AI gateway — Note.AI.Search/Embed/Similar —
/// bound to the kind's declared embedding configuration, proven over the in-memory stack.</summary>
public sealed class EntityAiGatewaySpec
{

    private static async Task<IHost> StartHostAsync()
    {
        EmbeddingRegistry.RegisterTypes([typeof(Note)]);

        var settings = new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
            ["Koan:Data:VectorDefaults:DefaultProvider"] = "inmemory",
            ["Koan:Data:AI:EmbeddingWorker:Enabled"] = "false",
        };

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings))
            .ConfigureServices(services => services.AddKoan())
            .Build();
        await host.StartAsync();
        return host;
    }

    private sealed class FixedWidthPipeline(int width) : IAiPipeline
    {
        public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AiEmbeddingsResponse { Vectors = [new float[width]], Model = "measured-embed" });

        public Task<AiChatResponse> Prompt(AiChatRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task search_finds_saved_entities_by_query()
    {
        using var host = await StartHostAsync();
        using var pipeline = Client.With(new FixedWidthPipeline(5));

        var note = await new Note { Title = "sunset over the harbor" }.Save();
        await Vector<Note>.Save(note.Id, new float[5]);

        var hits = await Note.AI.Search("sunset", limit: 5);

        hits.Should().ContainSingle(e => e.Id == note.Id);
    }

    [Fact]
    public async Task search_scored_returns_similarity_with_entities()
    {
        using var host = await StartHostAsync();
        using var pipeline = Client.With(new FixedWidthPipeline(5));

        var note = await new Note { Title = "dawn over the hills" }.Save();
        await Vector<Note>.Save(note.Id, new float[5]);

        var scored = await Note.AI.SearchScored("dawn", limit: 5);

        scored.Should().ContainSingle(m => m.Entity.Id == note.Id);
        scored[0].Similarity.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task embed_routes_through_the_declared_configuration()
    {
        using var host = await StartHostAsync();
        using var pipeline = Client.With(new FixedWidthPipeline(5));

        var vector = await Note.AI.Embed(new Note { Title = "embed me" });

        vector.Should().HaveCount(5);
    }

    [Fact]
    public async Task similar_excludes_the_source_entity()
    {
        using var host = await StartHostAsync();
        using var pipeline = Client.With(new FixedWidthPipeline(5));

        var note = await new Note { Title = "the source" }.Save();
        await Vector<Note>.Save(note.Id, new float[5]);

        var similar = await Note.AI.Similar(note, limit: 5);

        similar.Should().NotContain(n => n.Id == note.Id, "find-similar excludes the source by default");
    }
}
