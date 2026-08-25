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
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => JsonResponse("""{"version":"0.11.4"}"""),
                "/api/tags" => JsonResponse("""{"models":[{"name":"phi3:mini","model":"phi3"}]}"""),
                "/api/ps" => JsonResponse("""{"models":[{"name":"phi3:mini","model":"phi3"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using var adapter = CreateAdapter(handler);

        var result = await adapter.InspectAsync(new AiSourceCandidate
        {
            Provider = "ollama",
            Endpoint = "http://localhost:11434"
        });

        result.Available.Should().BeTrue();
        result.VersionAvailable.Should().BeTrue();
        result.Version.Should().Be("0.11.4");
        result.ModelsAvailable.Should().BeTrue();
        result.Models.Should().Equal("phi3:mini");
        result.ResidentModelsAvailable.Should().BeTrue();
        result.ResidentModels.Should().Equal("phi3:mini");
        result.Capabilities.Should().Contain("Chat");
        handler.Requests.Select(request => request.Uri.AbsolutePath)
            .Should().Equal("/api/version", "/api/tags", "/api/ps");
    }

    [Fact]
    public async Task Inspection_distinguishes_an_empty_catalog_from_unavailable_residency()
    {
        var handler = new RecordingHandler((request, _) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/version" => JsonResponse("""{"version":"0.11.4"}"""),
                "/api/tags" => JsonResponse("""{"models":[]}"""),
                "/api/ps" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        using var adapter = CreateAdapter(handler);

        var result = await adapter.InspectAsync(new AiSourceCandidate
        {
            Provider = "ollama",
            Endpoint = "http://localhost:11434"
        });

        result.Available.Should().BeTrue();
        result.ModelsAvailable.Should().BeTrue();
        result.Models.Should().BeEmpty();
        result.ResidentModelsAvailable.Should().BeFalse();
        result.ResidentModels.Should().BeEmpty();
        result.Detail.Should().Contain("resident models").And.Contain("503");
    }

    [Fact]
    public async Task Inspection_reports_unreachable_when_no_provider_facet_answers()
    {
        var handler = new RecordingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var adapter = CreateAdapter(handler);

        var result = await adapter.InspectAsync(new AiSourceCandidate
        {
            Provider = "ollama",
            Endpoint = "http://localhost:11434"
        });

        result.Available.Should().BeFalse();
        result.VersionAvailable.Should().BeFalse();
        result.ModelsAvailable.Should().BeFalse();
        result.ResidentModelsAvailable.Should().BeFalse();
        result.Detail.Should().Contain("provider version").And
            .Contain("installed models").And
            .Contain("resident models");
    }

    [Fact]
    public async Task Chat_with_tools_posts_to_chat_endpoint_and_surfaces_native_tool_calls()
    {
        var handler = new RecordingHandler((request, body) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/chat");
            var payload = JObject.Parse(body!);
            payload.Value<string>("model").Should().Be("qwen3");
            payload.Value<bool>("stream").Should().BeFalse();
            var messages = payload.Value<JArray>("messages")!;
            messages.Should().HaveCount(2);
            messages[0].Value<string>("role").Should().Be("system");
            messages[0].Value<string>("content").Should().Be("Use the catalog.");
            messages[1].Value<string>("role").Should().Be("user");
            var tools = payload.Value<JArray>("tools")!;
            tools.Should().HaveCount(1);
            tools[0].Value<string>("type").Should().Be("function");
            var function = tools[0].Value<JObject>("function")!;
            function.Value<string>("name").Should().Be("product_search");
            function.Value<string>("description").Should().Be("Find products");
            function.Value<JObject>("parameters")!.Value<string>("type").Should().Be("object");
            return JsonResponse(
                """{"model":"qwen3","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"product_search","arguments":{"query":"studio lamp"}}}]},"done":true,"done_reason":"stop"}""");
        });
        using var adapter = CreateAdapter(handler, "qwen3");

        var response = await adapter.Chat(new AiChatRequest
        {
            Messages = [new AiMessage("system", "Use the catalog."), new AiMessage("user", "Need lighting.")],
            Tools = [new AiToolDefinition("product_search", "Find products",
                """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""")]
        });

        response.Model.Should().Be("qwen3");
        response.FinishReason.Should().Be("stop");
        var call = response.ToolCalls.Should().ContainSingle().Subject;
        call.Name.Should().Be("product_search");
        call.ArgumentsJson.Should().Be("""{"query":"studio lamp"}""");
        handler.Requests.Select(request => request.Uri.AbsolutePath).Should().Equal("/api/chat");
    }

    [Fact]
    public async Task Native_tool_call_arguments_accept_the_string_form_older_servers_send()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            """{"model":"qwen3","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"product_search","arguments":"{\"query\":\"lamp\"}"}}]},"done":true,"done_reason":"stop"}"""));
        using var adapter = CreateAdapter(handler, "qwen3");

        var response = await adapter.Chat(new AiChatRequest
        {
            Messages = [new AiMessage("user", "Need lighting.")],
            Tools = [new AiToolDefinition("product_search", "Find products", "{}")]
        });

        response.ToolCalls.Should().ContainSingle();
        response.ToolCalls![0].ArgumentsJson.Should().Be("""{"query":"lamp"}""");
    }

    [Fact]
    public async Task Chat_with_tools_falls_back_to_flat_prompt_when_chat_endpoint_refuses()
    {
        var handler = new RecordingHandler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/chat")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":"does not support tools"}""")
                };
            }

            request.RequestUri.AbsolutePath.Should().Be("/api/generate");
            // Roles are flattened into the prompt, which still carries the system text.
            JObject.Parse(body!).Value<string>("prompt").Should().StartWith("[system]");
            return JsonResponse("""{"model":"phi3","response":"Answer from priors.","done":true,"done_reason":"stop"}""");
        });
        using var adapter = CreateAdapter(handler);

        var response = await adapter.Chat(new AiChatRequest
        {
            Messages = [new AiMessage("system", "Text protocol instructions."), new AiMessage("user", "Need lighting.")],
            Tools = [new AiToolDefinition("product_search", "Find products", "{}")]
        });

        response.Text.Should().Be("Answer from priors.");
        response.ToolCalls.Should().BeNull();
        handler.Requests.Select(request => request.Uri.AbsolutePath).Should().Equal("/api/chat", "/api/generate");
    }

    [Fact]
    public async Task Chat_without_any_endpoint_names_both_configuration_remedies()
    {
        // The exact state a sourceless boot leaves: no configured endpoints, so the constructor
        // never set a BaseAddress and discovery contributed nothing.
        using var adapter = new OllamaAdapter(
            new HttpClient(new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))),
            NullLogger<OllamaAdapter>.Instance,
            new OllamaOptions { Endpoints = [], DefaultModel = "phi3" });

        var act = () => adapter.Chat(new AiChatRequest
        {
            Messages = [new AiMessage("user", "Ping")]
        });

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("ConnectionStrings:Ollama").And
            .Contain("Koan:Ai:Ollama:Endpoints");
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
