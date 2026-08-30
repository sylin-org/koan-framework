using System.Net;
using System.Net.Http;
using System.Text;
using AwesomeAssertions;
using Koan.AI.Connector.LlamaCpp.Options;
using Koan.AI.Contracts.Models;
using Koan.Core.Adapters;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.AI.Connector.LlamaCpp.Tests;

/// <summary>Behavioral proof of the llama.cpp adapter against the deterministic llama-server
/// wire-contract service (ARCH-0120 posture): real sockets, real SSE bytes, real status codes.</summary>
public sealed class LlamaCppAdapterWireSpecs : IClassFixture<LlamaServerWireFixture>
{
    private readonly LlamaServerWireFixture _fixture;

    public LlamaCppAdapterWireSpecs(LlamaServerWireFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private LlamaCppAdapter CreateAdapter(Action<LlamaCppOptions>? configure = null)
    {
        var options = new LlamaCppOptions
        {
            Endpoints = [_fixture.BaseUrl],
            Readiness = new AdapterReadinessConfiguration { Timeout = TimeSpan.FromSeconds(5) }
        };
        configure?.Invoke(options);
        return new LlamaCppAdapter(new HttpClient(), NullLogger<LlamaCppAdapter>.Instance,
            new AdaptersReadinessOptions(), options);
    }

    private static AiChatRequest ChatRequest(string? model = null, string content = "hi") => new()
    {
        Model = model,
        Messages = [new AiMessage("user", content)]
    };

    [Fact]
    public async Task Chat_posts_openai_payload_with_bearer_auth_and_returns_completion()
    {
        var adapter = CreateAdapter(options =>
        {
            options.DefaultModel = LlamaServerWireFixture.ModelId;
            options.ApiKey = "secret-key";
        });

        var response = await adapter.Chat(ChatRequest(content: "ping"), CancellationToken.None);

        response.Text.Should().Be("pong: ping");
        response.Model.Should().Be(LlamaServerWireFixture.ModelId);
        response.FinishReason.Should().Be("stop");
        response.AdapterId.Should().Be("llamacpp");

        var request = _fixture.Requests.Single(r => r.Path == "/v1/chat/completions");
        request.Authorization.Should().Be("Bearer secret-key");
        var payload = JObject.Parse(request.Body);
        payload.Value<bool>("stream").Should().BeFalse();
        payload.Value<string>("model").Should().Be(LlamaServerWireFixture.ModelId);
        payload["messages"]!.Value<JArray>()!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Chat_normalizes_endpoint_with_trailing_v1()
    {
        var adapter = CreateAdapter(options =>
        {
            options.Endpoints = [_fixture.BaseUrl + "/v1"];
            options.DefaultModel = LlamaServerWireFixture.ModelId;
        });

        var response = await adapter.Chat(ChatRequest(content: "v1"), CancellationToken.None);
        response.Text.Should().Be("pong: v1");
        _fixture.Requests.Single(r => r.Path == "/v1/chat/completions").Should().NotBeNull();
    }

    [Fact]
    public async Task Chat_without_any_model_throws_correctively()
    {
        var adapter = CreateAdapter();
        var act = () => adapter.Chat(ChatRequest(model: null), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Chat_with_unknown_model_surfaces_provider_refusal()
    {
        var adapter = CreateAdapter(options => options.DefaultModel = LlamaServerWireFixture.ModelId);
        var act = () => adapter.Chat(ChatRequest(model: "not-loaded"), CancellationToken.None);
        (await act.Should().ThrowAsync<HttpRequestException>()).And.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stream_yields_deltas_in_order_until_done()
    {
        var adapter = CreateAdapter(options => options.DefaultModel = LlamaServerWireFixture.ModelId);

        var text = new StringBuilder();
        await foreach (var chunk in adapter.Stream(ChatRequest(content: "stream"), CancellationToken.None))
        {
            chunk.AdapterId.Should().Be("llamacpp");
            chunk.Model.Should().Be(LlamaServerWireFixture.ModelId);
            text.Append(chunk.DeltaText);
        }

        text.ToString().Should().Be("Hello pong: stream");
        var request = _fixture.Requests.Where(r => r.Path == "/v1/chat/completions").Last();
        JObject.Parse(request.Body).Value<bool>("stream").Should().BeTrue();
    }

    [Fact]
    public async Task Stream_tolerates_malformed_sse_line()
    {
        _fixture.InjectMalformedSseLine = true;
        try
        {
            var adapter = CreateAdapter(options => options.DefaultModel = LlamaServerWireFixture.ModelId);
            var text = new StringBuilder();
            await foreach (var chunk in adapter.Stream(ChatRequest(), CancellationToken.None))
                text.Append(chunk.DeltaText);
            text.ToString().Should().Be("Hello pong: hi");
        }
        finally
        {
            _fixture.InjectMalformedSseLine = false;
        }
    }

    [Fact]
    public async Task Stream_cancellation_surfaces_operationcanceled()
    {
        var adapter = CreateAdapter(options => options.DefaultModel = LlamaServerWireFixture.ModelId);
        using var cts = new CancellationTokenSource(0);
        var act = async () =>
        {
            await foreach (var _ in adapter.Stream(ChatRequest(), cts.Token)) { }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Embed_returns_vectors_dimension_and_records_payload()
    {
        var adapter = CreateAdapter(options =>
        {
            options.DefaultModel = LlamaServerWireFixture.ModelId;
            options.ApiKey = "embed-key";
        });

        var response = await adapter.Embed(new AiEmbeddingsRequest
        {
            Model = LlamaServerWireFixture.ModelId,
            Input = ["one", "two"]
        }, CancellationToken.None);

        response.Vectors.Should().HaveCount(2);
        response.Dimension.Should().Be(LlamaServerWireFixture.EmbeddingDimension);
        response.Vectors[0].Should().Equal(LlamaServerWireFixture.EmbeddingVector(0));
        response.Vectors[1].Should().Equal(LlamaServerWireFixture.EmbeddingVector(1));

        var request = _fixture.Requests.Single(r => r.Path == "/v1/embeddings");
        request.Authorization.Should().Be("Bearer embed-key");
        var payload = JObject.Parse(request.Body);
        payload["input"]!.Value<JArray>()!.Should().HaveCount(2);
    }

    [Fact]
    public async Task Embed_without_embedding_support_fails_loudly()
    {
        _fixture.EmbeddingsDisabled = true;
        try
        {
            var adapter = CreateAdapter(options => options.DefaultModel = LlamaServerWireFixture.ModelId);
            var act = () => adapter.Embed(new AiEmbeddingsRequest
            {
                Model = LlamaServerWireFixture.ModelId,
                Input = ["x"]
            }, CancellationToken.None);
            (await act.Should().ThrowAsync<HttpRequestException>()).And.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            _fixture.EmbeddingsDisabled = false;
        }
    }

    [Fact]
    public async Task Readiness_reports_ready_with_default_model_listed()
    {
        var adapter = CreateAdapter(options => options.DefaultModel = LlamaServerWireFixture.ModelId);
        await adapter.WaitForReadiness(TimeSpan.FromSeconds(5));
        adapter.IsReady.Should().BeTrue();
        adapter.ReadinessState.Should().Be(AdapterReadinessState.Ready);
    }

    [Fact]
    public async Task Readiness_reports_degraded_when_default_model_absent()
    {
        var adapter = CreateAdapter(options => options.DefaultModel = "not-the-loaded-model");
        await adapter.WaitForReadiness(TimeSpan.FromSeconds(5));
        // Degraded still admits calls (IsReady counts it); the state must name it honestly.
        adapter.ReadinessState.Should().Be(AdapterReadinessState.Degraded);
    }

    [Fact]
    public async Task Readiness_fails_while_the_model_is_loading()
    {
        _fixture.HealthLoading = true;
        try
        {
            var adapter = CreateAdapter(options =>
            {
                options.DefaultModel = LlamaServerWireFixture.ModelId;
                options.Readiness = new AdapterReadinessConfiguration { Timeout = TimeSpan.FromSeconds(2) };
            });
            var act = () => adapter.Chat(ChatRequest(), CancellationToken.None);
            await act.Should().ThrowAsync<AdapterNotReadyException>();
            adapter.ReadinessState.Should().Be(AdapterReadinessState.Failed);
        }
        finally
        {
            _fixture.HealthLoading = false;
        }
    }

    [Fact]
    public async Task ListModels_reports_the_loaded_model()
    {
        var adapter = CreateAdapter(options => options.DefaultModel = LlamaServerWireFixture.ModelId);
        var models = await adapter.ListModels(CancellationToken.None);
        models.Should().ContainSingle();
        models[0].Name.Should().Be(LlamaServerWireFixture.ModelId);
        models[0].AdapterId.Should().Be("llamacpp");
    }
}
