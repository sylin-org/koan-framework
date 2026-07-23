using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Koan.AI.Connector.Ollama;
using Koan.AI.Connector.Ollama.Options;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Adapters;

public sealed class OllamaAdapterSpec
{
    [Fact]
    public async Task Chat_serializes_native_generate_request_and_maps_response()
    {
        var handler = new RecordingHandler((request, body) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/generate");
            var payload = JObject.Parse(body!);
            payload.Value<string>("model").Should().Be("phi3");
            payload.Value<string>("prompt").Should().Be("Ping");
            payload.Value<bool>("stream").Should().BeFalse();
            return JsonResponse("""{"model":"phi3","response":"Done","done":true,"done_reason":"stop"}""");
        });
        using var adapter = CreateAdapter(handler);

        var response = await adapter.Chat(new AiChatRequest
        {
            Messages = [new AiMessage("user", "Ping")]
        });

        response.Text.Should().Be("Done");
        response.Model.Should().Be("phi3");
        response.FinishReason.Should().Be("stop");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Stream_reads_native_json_lines_in_order()
    {
        var handler = new RecordingHandler((request, body) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/generate");
            JObject.Parse(body!).Value<bool>("stream").Should().BeTrue();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"model\":\"phi3\",\"response\":\"Hel\",\"done\":false}\n" +
                    "{\"model\":\"phi3\",\"response\":\"lo\",\"done\":true}\n",
                    Encoding.UTF8,
                    "application/x-ndjson")
            };
        });
        using var adapter = CreateAdapter(handler);

        var chunks = new List<string>();
        await foreach (var chunk in adapter.Stream(new AiChatRequest
        {
            Messages = [new AiMessage("user", "Hello?")]
        }))
        {
            chunk.DeltaText.Should().NotBeNull();
            chunks.Add(chunk.DeltaText!);
        }

        chunks.Should().Equal("Hel", "lo");
    }

    [Fact]
    public async Task Embed_uses_native_endpoint_for_each_input_and_preserves_order()
    {
        var call = 0;
        var handler = new RecordingHandler((request, body) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/embeddings");
            var payload = JObject.Parse(body!);
            payload.Value<string>("model").Should().Be("nomic-embed");
            call++;
            return JsonResponse(call == 1
                ? """{"embedding":[1.0,0.0,0.5]}"""
                : """{"embedding":[0.1,0.2,0.3]}""");
        });
        using var adapter = CreateAdapter(handler, "nomic-embed");

        var response = await adapter.Embed(new AiEmbeddingsRequest
        {
            Input = ["first", "second"]
        });

        response.Model.Should().Be("nomic-embed");
        response.Dimension.Should().Be(3);
        response.Vectors.Should().HaveCount(2);
        response.Vectors[0].Should().Equal(1f, 0f, 0.5f);
        response.Vectors[1].Should().Equal(0.1f, 0.2f, 0.3f);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Inspection_uses_the_candidate_ollama_protocol_without_registering_a_source()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/tags");
            return JsonResponse("""{"models":[{"name":"phi3:mini","model":"phi3"}]}""");
        });
        using var adapter = CreateAdapter(handler);

        var result = await adapter.InspectAsync(new AiSourceCandidate
        {
            Provider = "ollama",
            Endpoint = "http://localhost:11434"
        });

        result.Available.Should().BeTrue();
        result.Models.Should().Equal("phi3:mini");
        result.Capabilities.Should().Contain("Chat");
    }

    private static OllamaAdapter CreateAdapter(RecordingHandler handler, string model = "phi3")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        return new OllamaAdapter(
            http,
            NullLogger<OllamaAdapter>.Instance,
            new OllamaOptions { Endpoints = ["http://localhost:11434"], DefaultModel = model });
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!, body));
            return responder(request, body);
        }
    }
}
