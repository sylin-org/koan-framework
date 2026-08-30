using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.AI.Connector.LlamaCpp.Tests;

/// <summary>Deterministic llama-server wire-contract service (ARCH-0120 posture): a real Kestrel
/// host speaking llama-server's documented OpenAI-compatible REST contract. HuggingFace is
/// download-gated in the proof environment, so no real GGUF can be fetched; this proves the full
/// wire path (sockets, SSE bytes, status codes, recorded requests) — model-inference behavior is
/// out of scope by nature and reported as such.</summary>
public sealed class LlamaServerWireFixture : IAsyncLifetime
{
    public const string ModelId = "test-gguf";
    public const int EmbeddingDimension = 8;

    private WebApplication? _app;
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests => _requests;
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Simulates llama-server still loading the model: <c>GET /health</c> answers 503.</summary>
    public bool HealthLoading { get; set; }

    /// <summary>Simulates a server started without <c>--embedding</c>: embeddings answer 400.</summary>
    public bool EmbeddingsDisabled { get; set; }

    /// <summary>Injects one malformed SSE line before the first good chunk.</summary>
    public bool InjectMalformedSseLine { get; set; }

    public string? ServerApiKey { get; set; }

    public async ValueTask InitializeAsync()
    {
        var port = GrabFreePort();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();

        app.MapGet("/health", () => HealthLoading
            ? Results.Json(new { error = new { message = "loading model" } }, statusCode: 503)
            : Results.Json(new { status = "ok" }));

        app.MapGet("/v1/models", () => Results.Json(new
        {
            @object = "list",
            data = new[] { new { id = ModelId, @object = "model", owned_by = "llama.cpp" } }
        }));

        app.MapPost("/v1/chat/completions", async (HttpContext context) =>
        {
            var body = await new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);
            var payload = JObject.Parse(body);
            _requests.Add(new RecordedRequest(
                context.Request.Path,
                body,
                context.Request.Headers.Authorization.ToString()));

            var requestedModel = payload.Value<string>("model");
            if (requestedModel is not null && !string.Equals(requestedModel, ModelId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new
                {
                    error = new { message = $"model '{requestedModel}' not found", type = "invalid_request_error" }
                }, statusCode: 404);
            }

            var lastUser = payload["messages"]?.LastOrDefault(m => m.Value<string>("role") == "user")?
                .Value<string>("content") ?? "";
            if (payload.Value<bool>("stream") == true)
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "text/event-stream";
                if (InjectMalformedSseLine)
                {
                    await context.Response.WriteAsync("data: {not-json}\n\n", context.RequestAborted);
                }

                foreach (var delta in new[] { "Hello", " ", "pong:", " ", lastUser })
                {
                    var frame = new JObject
                    {
                        ["id"] = "chatcmpl-1",
                        ["object"] = "chat.completion.chunk",
                        ["model"] = ModelId,
                        ["choices"] = new JArray { new JObject
                        {
                            ["index"] = 0,
                            ["delta"] = new JObject { ["content"] = delta }
                        } }
                    };
                    await context.Response.WriteAsync($"data: {frame.ToString(Newtonsoft.Json.Formatting.None)}\n\n", context.RequestAborted);
                }

                await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
                return Results.Empty;
            }

            var completion = new JObject
            {
                ["id"] = "chatcmpl-1",
                ["object"] = "chat.completion",
                ["created"] = 1_900_000_000L,
                ["model"] = ModelId,
                ["choices"] = new JArray { new JObject
                {
                    ["index"] = 0,
                    ["message"] = new JObject { ["role"] = "assistant", ["content"] = $"pong: {lastUser}" },
                    ["finish_reason"] = "stop"
                } },
                ["usage"] = new JObject { ["prompt_tokens"] = 1, ["completion_tokens"] = 1, ["total_tokens"] = 2 }
            };
            // Kestrel's Results.Json serializes with System.Text.Json, which mangles JToken graphs;
            // llama-server speaks plain JSON, so the fixture serializes with Newtonsoft and returns text.
            return Results.Text(completion.ToString(Newtonsoft.Json.Formatting.None), "application/json");
        });

        app.MapPost("/v1/embeddings", async (HttpContext context) =>
        {
            var body = await new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);
            var payload = JObject.Parse(body);
            _requests.Add(new RecordedRequest(
                context.Request.Path,
                body,
                context.Request.Headers.Authorization.ToString()));

            if (EmbeddingsDisabled)
            {
                return Results.Json(new
                {
                    error = new { message = "this server was started without --embedding and cannot serve embeddings" }
                }, statusCode: 400);
            }

            var input = payload["input"] as JArray ?? new JArray();
            var embedding = new JObject
            {
                ["object"] = "list",
                ["model"] = ModelId,
                ["data"] = new JArray(input.Select((_, index) => new JObject
                {
                    ["object"] = "embedding",
                    ["index"] = index,
                    ["embedding"] = new JArray(EmbeddingVector(index).Select(v => (JToken)new JValue(v)))
                }))
            };
            return Results.Text(embedding.ToString(Newtonsoft.Json.Formatting.None), "application/json");
        });

        _app = app;
        await app.StartAsync();
        BaseUrl = $"http://127.0.0.1:{port}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            try { await _app.StopAsync(); } catch { }
            await _app.DisposeAsync();
        }
    }

    public static float[] EmbeddingVector(int index) => Enumerable
        .Range(0, EmbeddingDimension)
        .Select(dimension => (float)Math.Sin(index + 1 + dimension))
        .ToArray();

    public void Reset() => _requests.Clear();

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}

public sealed record RecordedRequest(string Path, string Body, string Authorization);
