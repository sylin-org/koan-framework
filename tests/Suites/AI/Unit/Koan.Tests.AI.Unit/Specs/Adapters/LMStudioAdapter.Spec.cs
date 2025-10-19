using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.AI.Connector.LMStudio;
using Koan.AI.Connector.LMStudio.Options;
using Koan.AI.Contracts.Models;
using Koan.Core.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Adapters;

public class LMStudioAdapterSpec
{
    [Fact]
    public async Task ChatAsync_serializes_payload_and_attaches_auth()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/models")
            {
                return JsonResponse("{\"data\":[{\"id\":\"phi3:mini\"}]}");
            }

            if (path == "/v1/chat/completions")
            {
                var response = new JObject
                {
                    ["id"] = "chat-1",
                    ["model"] = "phi3:mini",
                    ["choices"] = new JArray
                    {
                        new JObject
                        {
                            ["index"] = 0,
                            ["message"] = new JObject
                            {
                                ["role"] = "assistant",
                                ["content"] = "Done"
                            },
                            ["finish_reason"] = "stop"
                        }
                    }
                };

                return JsonResponse(response.ToString());
            }

            throw new InvalidOperationException($"Unexpected request path '{path}'");
        });

        var adapter = CreateAdapter(handler, new LMStudioOptions
        {
            BaseUrl = "http://localhost:1234",
            DefaultModel = "phi3:mini",
            ApiKey = "secret"
        });

        await adapter.WaitForReadinessAsync(TimeSpan.FromSeconds(1));

        var response = await adapter.ChatAsync(new AiChatRequest
        {
            Messages = new List<AiMessage>
            {
                new("user", "Ping")
            }
        });

        response.Text.Should().Be("Done");
        response.Model.Should().Be("phi3:mini");
        response.AdapterId.Should().Be("lmstudio");

        var chatRequest = handler.Requests.Single(r => r.Uri.AbsolutePath == "/v1/chat/completions");
        chatRequest.Authorization.Should().Be("Bearer secret");

        var payload = JObject.Parse(chatRequest.Body!);
        payload.Value<string>("model").Should().Be("phi3:mini");
        payload.Value<bool>("stream").Should().BeFalse();
        payload["messages"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task StreamAsync_yields_chunks_from_sse_payload()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/models")
            {
                return JsonResponse("{\"data\":[{\"id\":\"phi3:mini\"}]}");
            }

            if (path == "/v1/chat/completions")
            {
                var streamPayload = "data: {\"id\":\"chunk-1\",\"model\":\"phi3:mini\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hel\"}}]}\n\n" +
                                    "data: {\"id\":\"chunk-2\",\"model\":\"phi3:mini\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"lo\"}}]}\n\n" +
                                    "data: [DONE]\n\n";

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(streamPayload, Encoding.UTF8)
                };

                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return response;
            }

            throw new InvalidOperationException($"Unexpected request path '{path}'");
        });

        var adapter = CreateAdapter(handler);

        var chunks = new List<AiChatChunk>();
        await foreach (var chunk in adapter.StreamAsync(new AiChatRequest
        {
            Messages = new List<AiMessage> { new("user", "Hello?") }
        }))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCount(2);
        chunks.Select(c => c.DeltaText).Should().BeEquivalentTo(new[] { "Hel", "lo" }, options => options.WithStrictOrdering());
        chunks.Select(c => c.Model).Distinct().Should().Equal("phi3:mini");

        handler.Requests.Should().Contain(r => r.Uri.AbsolutePath == "/v1/models");
    }

    [Fact]
    public async Task EmbedAsync_maps_inputs_and_returns_vectors()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/models")
            {
                return JsonResponse("{\"data\":[{\"id\":\"phi3:mini\"}]}");
            }

            if (path == "/v1/embeddings")
            {
                var response = new JObject
                {
                    ["model"] = "nomic-embed",
                    ["data"] = new JArray
                    {
                        new JObject
                        {
                            ["index"] = 0,
                            ["embedding"] = new JArray(1.0, 0.0, 0.5)
                        },
                        new JObject
                        {
                            ["index"] = 1,
                            ["embedding"] = new JArray(0.1, 0.2, 0.3)
                        }
                    }
                };

                return JsonResponse(response.ToString());
            }

            throw new InvalidOperationException($"Unexpected request path '{path}'");
        });

        var adapter = CreateAdapter(handler);
        await adapter.WaitForReadinessAsync(TimeSpan.FromSeconds(1));

        var response = await adapter.EmbedAsync(new AiEmbeddingsRequest
        {
            Model = "nomic-embed",
            Input = new List<string> { "a", "b" }
        });

        response.Model.Should().Be("nomic-embed");
        response.Vectors.Should().HaveCount(2);
        response.Dimension.Should().Be(3);

        var embedRequest = handler.Requests.Single(r => r.Uri.AbsolutePath == "/v1/embeddings");
        var payload = JObject.Parse(embedRequest.Body!);
        payload["input"]!.Should().HaveCount(2);
    }

    [Fact]
    public async Task WaitForReadinessAsync_marks_adapter_degraded_when_default_missing()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/models")
            {
                return JsonResponse("{\"data\":[{\"id\":\"phi3:mini\"}]}");
            }

            throw new InvalidOperationException("Readiness should only query /v1/models");
        });

        var adapter = CreateAdapter(handler, new LMStudioOptions
        {
            BaseUrl = "http://localhost:1234",
            DefaultModel = "missing-model"
        });

        await adapter.WaitForReadinessAsync(TimeSpan.FromSeconds(1));

        adapter.ReadinessState.Should().Be(AdapterReadinessState.Degraded);
        adapter.IsReady.Should().BeTrue();
    }

    private static LMStudioAdapter CreateAdapter(RecordingHandler handler, LMStudioOptions? options = null)
    {
        var http = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var readiness = new AdaptersReadinessOptions { DefaultTimeout = TimeSpan.FromSeconds(1) };
        var effectiveOptions = options ?? new LMStudioOptions
        {
            BaseUrl = "http://localhost:1234",
            DefaultModel = "phi3:mini"
        };

        return new LMStudioAdapter(http, NullLogger<LMStudioAdapter>.Instance, config, readiness, effectiveOptions);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var authorization = request.Headers.Authorization?.ToString();
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, authorization));

            return _responder(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Authorization);
}

