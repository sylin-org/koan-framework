using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace S7.Meridian.Tests.Integration;

public sealed class PipelineE2ETests
{
    private readonly ITestOutputHelper _output;

    public PipelineE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task EndToEnd_UploadExtractMergeRender_Success()
    {
        var (provider, storage, _) = BuildServiceProvider();
        var previousHost = AppHost.Current;

        await using var scope = provider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;

        try
        {
            AppHost.Current = scopedProvider;
            KoanEnv.TryInitialize(scopedProvider);

            using var partition = EntityContext.Partition($"meridian-e2e-{Guid.NewGuid():N}");
            using var aiScope = Ai.With(new FakeAi());
            var ct = CancellationToken.None;

            var analysisType = new AnalysisType
            {
                Name = "Test Analysis",
                Description = "Integration test analysis",
                Instructions = "Summarize financial metrics with supporting context.",
                OutputTemplate = "# Test Report\n\nRevenue: {{revenue}}\nEmployees: {{employees}}",
                RequiredSourceTypes = new List<string> { MeridianConstants.SourceTypes.AuditedFinancial }
            };
            await analysisType.Save(ct);

            var pipeline = new DocumentPipeline
            {
                Name = "Test Pipeline",
                AnalysisTypeId = analysisType.Id,
                SchemaJson = """
                {
                    "type": "object",
                    "properties": {
                        "revenue": { "type": "number" },
                        "employees": { "type": "number" }
                    },
                    "required": [ "revenue", "employees" ]
                }
                """
            };
            await pipeline.Save(ct);

            const string documentText = "Our company had annual revenue of $47.2M in FY2023. We have 150 employees.";
            var storageKey = await storage.StoreStringAsync(documentText, "financials.txt", "text/plain", ct);

            var document = new SourceDocument
            {
                PipelineId = pipeline.Id,
                OriginalFileName = "financials.txt",
                StorageKey = storageKey,
                SourceType = MeridianConstants.SourceTypes.AuditedFinancial,
                MediaType = "text/plain",
                Size = Encoding.UTF8.GetByteCount(documentText),
                Status = DocumentProcessingStatus.Pending,
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await document.Save(ct);

            var job = new ProcessingJob
            {
                PipelineId = pipeline.Id,
                Status = JobStatus.Pending,
                DocumentIds = new List<string> { document.Id }
            };
            await job.Save(ct);

            var processor = scopedProvider.GetRequiredService<IPipelineProcessor>();
            await processor.ProcessAsync(job, ct);

            var extractions = await ExtractedField.Query(e => e.PipelineId == pipeline.Id, ct);
            var fieldSummary = string.Join(", ", extractions.Select(e => $"{e.FieldPath}={e.ValueJson}"));
            _output.WriteLine($"Extracted fields: {fieldSummary}");

            var revenue = extractions.Should().ContainSingle(e => e.FieldPath == "$.revenue", "because the available fields were {0}", fieldSummary).Subject;
            revenue.ValueJson.Should().Be("47.2");
            revenue.Confidence.Should().BeGreaterThan(0.5);
            revenue.PassageId.Should().NotBeNull();
            revenue.Evidence.Should().NotBeNull();
            revenue.Evidence.Span.Should().NotBeNull();
            revenue.MergeStrategy.Should().Be("sourcePrecedence");

            var employees = extractions.Should().ContainSingle(e => e.FieldPath == "$.employees", "because the available fields were {0}", fieldSummary).Subject;
            employees.ValueJson.Should().Be("150");
            employees.Confidence.Should().BeGreaterThan(0.5);
            employees.PassageId.Should().NotBeNull();

            var deliverables = await Deliverable.Query(d => d.PipelineId == pipeline.Id, ct);
            var deliverable = deliverables.Should().ContainSingle().Subject;
            deliverable.Markdown.Should().Contain("Revenue: 47.2[^1]");
            deliverable.Markdown.Should().Contain("Employees: 150");
            deliverable.Markdown.Should().Contain("[^1]:");
            deliverable.Markdown.Should().Contain("financials.txt");

            var persistedPipeline = await DocumentPipeline.Get(pipeline.Id, ct);
            persistedPipeline.Should().NotBeNull();
            persistedPipeline!.Quality.HighConfidence.Should().BeGreaterThan(0);
        }
        finally
        {
            if (ReferenceEquals(AppHost.Current, scopedProvider))
            {
                AppHost.Current = previousHost;
            }

            await scope.DisposeAsync();
            await (provider as IAsyncDisposable ?? provider).DisposeAsync();
        }
    }

    private static (ServiceProvider Provider, FakeDocumentStorage Storage, InMemoryEmbeddingCache Cache) BuildServiceProvider()
    {
        var configurationValues = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Provider"] = "Memory",
            ["Meridian:Retrieval:TopK"] = "8",
            ["Meridian:Retrieval:Alpha"] = "0.5",
            ["Meridian:Retrieval:MmrLambda"] = "0.7",
            ["Meridian:Retrieval:MaxTokensPerField"] = "2000",
            ["Meridian:Extraction:ParallelismDegree"] = "1",
            ["Meridian:Extraction:Temperature"] = "0.0",
            ["Meridian:Extraction:MaxOutputTokens"] = "256",
            ["Meridian:Confidence:LowThreshold"] = "0.5",
            ["Meridian:Confidence:HighThreshold"] = "0.9",
            ["Meridian:Merge:EnableExplainability"] = "true",
            ["Meridian:Merge:EnableCitations"] = "true",
            ["Meridian:Merge:EnableNormalizedComparison"] = "true",
            ["Meridian:Merge:DefaultSourcePrecedence:0"] = MeridianConstants.SourceTypes.AuditedFinancial,
            ["Meridian:Merge:DefaultSourcePrecedence:1"] = MeridianConstants.SourceTypes.VendorPrescreen,
            ["Meridian:Merge:DefaultSourcePrecedence:2"] = MeridianConstants.SourceTypes.KnowledgeBase,
            ["Meridian:Merge:DefaultSourcePrecedence:3"] = MeridianConstants.SourceTypes.Unclassified,
            ["Meridian:Merge:Policies:$.revenue:Strategy"] = "sourcePrecedence",
            ["Meridian:Merge:Policies:$.revenue:SourcePrecedence:0"] = MeridianConstants.SourceTypes.AuditedFinancial,
            ["Meridian:Merge:Policies:$.revenue:SourcePrecedence:1"] = MeridianConstants.SourceTypes.VendorPrescreen,
            ["Meridian:Merge:Policies:$.revenue:SourcePrecedence:2"] = MeridianConstants.SourceTypes.KnowledgeBase,
            ["Meridian:Merge:Policies:$.revenue:Transform"] = "normalizeToUsd",
            ["Meridian:Merge:Policies:$.employees:Strategy"] = "latest"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues!)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddKoan();

        var storage = new FakeDocumentStorage();
        var deliverableStorage = new FakeDeliverableStorage();
        var cache = new InMemoryEmbeddingCache();

        services.AddSingleton<IDocumentStorage>(storage);
        services.AddSingleton<IDeliverableStorage>(deliverableStorage);
        services.AddSingleton<IEmbeddingCache>(cache);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MeridianOptions>>().Value);

        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), storage, cache);
    }

    private sealed class FakeDocumentStorage : IDocumentStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.OrdinalIgnoreCase);

        public async Task<string> StoreAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct).ConfigureAwait(false);
            var key = $"fake/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
            _store[key] = ms.ToArray();
            return key;
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
        {
            if (!_store.TryGetValue(storageKey, out var bytes))
            {
                throw new InvalidOperationException($"Storage key '{storageKey}' not found.");
            }

            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task<string> StoreStringAsync(string content, string fileName, string? contentType, CancellationToken ct = default)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var key = $"fake/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
            _store[key] = bytes;
            return Task.FromResult(key);
        }
    }

    private sealed class FakeDeliverableStorage : IDeliverableStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.OrdinalIgnoreCase);

        public async Task<string> StoreAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct).ConfigureAwait(false);
            var key = $"deliverables/{Guid.NewGuid():N}/{fileName}";
            _store[key] = ms.ToArray();
            return key;
        }
    }

    private sealed class InMemoryEmbeddingCache : IEmbeddingCache
    {
        private sealed record CacheEntry(float[] Embedding, DateTimeOffset CachedAt);

        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Task<CachedEmbedding?> GetAsync(string contentHash, string modelId, string entityTypeName, CancellationToken ct = default)
        {
            var key = ComposeKey(contentHash, modelId, entityTypeName);
            if (!_entries.TryGetValue(key, out var entry))
            {
                return Task.FromResult<CachedEmbedding?>(null);
            }

            var cached = new CachedEmbedding
            {
                ContentHash = contentHash,
                ModelId = modelId,
                Embedding = entry.Embedding,
                Dimension = entry.Embedding.Length,
                CachedAt = entry.CachedAt
            };

            return Task.FromResult<CachedEmbedding?>(cached);
        }

        public Task SetAsync(string contentHash, string modelId, float[] embedding, string entityTypeName, CancellationToken ct = default)
        {
            var key = ComposeKey(contentHash, modelId, entityTypeName);
            _entries[key] = new CacheEntry(embedding, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task<int> FlushAsync(CancellationToken ct = default)
        {
            var count = _entries.Count;
            _entries.Clear();
            return Task.FromResult(count);
        }

        public Task<CacheStats> GetStatsAsync(CancellationToken ct = default)
        {
            var total = _entries.Count;
            DateTimeOffset? oldest = null;
            DateTimeOffset? newest = null;

            foreach (var entry in _entries.Values)
            {
                if (oldest is null || entry.CachedAt < oldest)
                {
                    oldest = entry.CachedAt;
                }

                if (newest is null || entry.CachedAt > newest)
                {
                    newest = entry.CachedAt;
                }
            }

            return Task.FromResult(new CacheStats(total, 0, oldest, newest));
        }

        private static string ComposeKey(string hash, string model, string entity)
            => $"{entity}:{model}:{hash}";
    }

    private sealed class FakeAi : IAi
    {
        public Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct = default)
        {
            var prompt = request.Messages.LastOrDefault()?.Content ?? string.Empty;
            var response = ResolveResponse(prompt);
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await PromptAsync(request, ct).ConfigureAwait(false);
            yield return new AiChatChunk { DeltaText = response.Text, Index = 0, Model = response.Model };
        }

        public Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
        {
            var vectors = request.Input.Select(ComputeVector).ToList();
            return Task.FromResult(new AiEmbeddingsResponse
            {
                Vectors = vectors,
                Model = request.Model ?? "fake-embedding",
                Dimension = vectors.Count > 0 ? vectors[0].Length : 0
            });
        }

        public async Task<string> PromptAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        {
            var response = await PromptAsync(new AiChatRequest
            {
                Model = model,
                Options = opts,
                Messages = new List<AiMessage> { new("user", message) }
            }, ct).ConfigureAwait(false);

            return response.Text;
        }

        public async IAsyncEnumerable<AiChatChunk> StreamAsync(string message, string? model = null, AiPromptOptions? opts = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var request = new AiChatRequest
            {
                Model = model,
                Options = opts,
                Messages = new List<AiMessage> { new("user", message) }
            };

            await foreach (var chunk in StreamAsync(request, ct))
            {
                yield return chunk;
            }
        }

        private static AiChatResponse ResolveResponse(string prompt)
        {
            var match = Regex.Match(prompt, @"Extract the value for '([^']+)'", RegexOptions.IgnoreCase);
            var field = match.Success ? match.Groups[1].Value : string.Empty;

            if (field.Equals("employees", StringComparison.OrdinalIgnoreCase))
            {
                return new AiChatResponse
                {
                    Text = "{\"value\":150,\"confidence\":0.91,\"passageIndex\":0}",
                    Model = "fake-model",
                    TokensOut = 5
                };
            }

            if (field.Equals("revenue", StringComparison.OrdinalIgnoreCase))
            {
                return new AiChatResponse
                {
                    Text = "{\"value\":47.2,\"confidence\":0.93,\"passageIndex\":0}",
                    Model = "fake-model",
                    TokensOut = 6
                };
            }

            return new AiChatResponse
            {
                Text = "{\"value\":null,\"confidence\":0.0,\"passageIndex\":null}",
                Model = "fake-model",
                TokensOut = 4
            };
        }

        private static float[] ComputeVector(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            var vector = new float[8];
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = bytes[i] / 255f;
            }
            return vector;
        }
    }
}
