using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace S7.Meridian.Tests.Integration;

public sealed class MergePoliciesTests
{
    [Fact]
    public async Task MergePolicies_ComprehensiveScenario_Works()
    {
        var (provider, deliverableStorage) = BuildServiceProvider();
        var previousHost = AppHost.Current;

        await using var scope = provider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;

        try
        {
            AppHost.Current = scopedProvider;
            KoanEnv.TryInitialize(scopedProvider);

            using var partition = EntityContext.Partition($"meridian-phase2-{Guid.NewGuid():N}");
            var ct = CancellationToken.None;
            var now = DateTime.UtcNow;

            var pipeline = new DocumentPipeline
            {
                Name = "Phase 2 Merge Policies",
                SchemaJson = """
                {
                    "type": "object",
                    "properties": {
                        "revenue": { "type": "number" },
                        "productLines": { "type": "array", "items": { "type": "string" } },
                        "complianceStatus": { "type": "string" },
                        "executiveSummary": { "type": "string" }
                    }
                }
                """,
                TemplateMarkdown = "# Merge Report\nRevenue: {{revenue}}\nProducts: {{productLines}}\nStatus: {{complianceStatus}}\nSummary: {{executiveSummary}}\n"
            };
            await pipeline.Save(ct);

            var financial = await CreateSourceDocumentAsync(pipeline.Id, "financial.pdf", MeridianConstants.SourceTypes.AuditedFinancial, now.AddMinutes(-30), ct);
            var vendor = await CreateSourceDocumentAsync(pipeline.Id, "vendor.docx", MeridianConstants.SourceTypes.VendorPrescreen, now.AddMinutes(-20), ct);
            var knowledge = await CreateSourceDocumentAsync(pipeline.Id, "knowledge.txt", MeridianConstants.SourceTypes.KnowledgeBase, now.AddMinutes(-10), ct);

            var passages = new Dictionary<string, Passage>
            {
                ["fin"] = await CreatePassageAsync(pipeline.Id, financial.Id, "Annual revenue reached $47.2M last year.", 12, "Financials", ct),
                ["vendor"] = await CreatePassageAsync(pipeline.Id, vendor.Id, "Revenue reported at $39.5M from vendor questionnaire.", 4, "Financial Overview", ct),
                ["kb"] = await CreatePassageAsync(pipeline.Id, knowledge.Id, "Estimated revenue of $40M across divisions.", 2, "Summary", ct)
            };

            var extractions = new List<ExtractedField>
            {
                CreateExtraction(pipeline.Id, "$.revenue", "\"$47.2M\"", 0.92, financial.Id, passages["fin"], now.AddMinutes(-29)),
                CreateExtraction(pipeline.Id, "$.revenue", "\"$39.5M\"", 0.82, vendor.Id, passages["vendor"], now.AddMinutes(-19)),
                CreateExtraction(pipeline.Id, "$.revenue", "\"$40M\"", 0.60, knowledge.Id, passages["kb"], now.AddMinutes(-9)),
                CreateExtraction(pipeline.Id, "$.productLines", "[\"Analytics\", \"Security\"]", 0.75, financial.Id, passages["fin"], now.AddMinutes(-28)),
                CreateExtraction(pipeline.Id, "$.productLines", "[\"analytics\", \"Compliance\"]", 0.70, vendor.Id, passages["vendor"], now.AddMinutes(-18)),
                CreateExtraction(pipeline.Id, "$.complianceStatus", "\"Approved\"", 0.65, financial.Id, passages["fin"], now.AddMinutes(-27)),
                CreateExtraction(pipeline.Id, "$.complianceStatus", "\"Approved\"", 0.60, vendor.Id, passages["vendor"], now.AddMinutes(-17)),
                CreateExtraction(pipeline.Id, "$.complianceStatus", "\"Pending\"", 0.80, knowledge.Id, passages["kb"], now.AddMinutes(-8)),
                CreateOverrideExtraction(pipeline.Id, "\"Manual summary created by compliance officer.\"", financial.Id, passages["fin"], now.AddMinutes(-5))
            };

            var merger = scopedProvider.GetRequiredService<IDocumentMerger>();
            var deliverable = await merger.MergeAsync(pipeline, extractions, ct);

            // Assertions: revenue transformed and precedence applied
            var mergedFields = await ExtractedField.Query(e => e.PipelineId == pipeline.Id, ct);
            var revenueField = mergedFields.First(f => f.FieldPath == "$.revenue" && f.MergeStrategy == "sourcePrecedence");
            var revenueValue = JToken.Parse(revenueField.ValueJson ?? "0");
            revenueValue.Value<decimal>().Should().Be(47200000m);
            revenueField.MergeStrategy.Should().Be("sourcePrecedence");

            // Products union with fuzzy dedupe
            var productsField = mergedFields.First(f => f.FieldPath == "$.productLines" && f.MergeStrategy?.StartsWith("collection", StringComparison.OrdinalIgnoreCase) == true);
            var productArray = JArray.Parse(productsField.ValueJson ?? "[]");
            productArray.Values<string>().Should().BeEquivalentTo(new[] { "Analytics", "Security", "Compliance" });

            // Consensus
            var statusField = mergedFields.First(f => f.FieldPath == "$.complianceStatus" && f.MergeStrategy == "consensus");
            var statusValue = JToken.Parse(statusField.ValueJson ?? "\"\"");
            statusValue.Value<string>().Should().Be("Approved");
            statusField.MergeStrategy.Should().Be("consensus");

            // Override logic
            var summaryField = mergedFields.First(f => f.FieldPath == "$.executiveSummary" && f.MergeStrategy == "override");
            var summaryValue = JToken.Parse(summaryField.ValueJson ?? "\"\"");
            summaryValue.Value<string>().Should().Be("Manual summary created by compliance officer.");
            summaryField.Confidence.Should().Be(1.0);
            summaryField.MergeStrategy.Should().Be("override");

            // Deliverable footnotes present
            deliverable.RenderedMarkdown.Should().Contain("[^1]:");
            deliverable.RenderedMarkdown.Should().Contain("financial.pdf");

            // Merge decisions persisted
            var decisions = await MergeDecision.Query(d => d.PipelineId == pipeline.Id, ct);
            decisions.Should().HaveCount(4);

            var revenueDecision = decisions.First(d => d.FieldPath == "$.revenue");
            revenueDecision.Strategy.Should().Be("sourcePrecedence");
            revenueDecision.RejectedExtractionIds.Should().HaveCount(2);
            JObject.Parse(revenueDecision.RuleConfigJson)["Strategy"]?.Value<string>().Should().Be("SourcePrecedence");

            var overrideDecision = decisions.First(d => d.FieldPath == "$.executiveSummary");
            overrideDecision.Strategy.Should().Be("override");
            overrideDecision.Explanation.Should().Contain("override");

            // Deliverable storage captured rendered bytes
            deliverableStorage.StoredPdfKeys.Should().Contain(deliverable.RenderedPdfKey);
        }
        finally
        {
            if (ReferenceEquals(AppHost.Current, scopedProvider))
            {
                AppHost.Current = previousHost;
            }

            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private static (ServiceProvider Provider, FakeDeliverableStorage DeliverableStorage) BuildServiceProvider()
    {
        var configurationValues = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Provider"] = "Memory",
            ["Meridian:Merge:EnableCitations"] = "true",
            ["Meridian:Merge:EnableExplainability"] = "true",
            ["Meridian:Merge:EnableNormalizedComparison"] = "true",
            ["Meridian:Merge:DefaultSourcePrecedence:0"] = MeridianConstants.SourceTypes.AuditedFinancial,
            ["Meridian:Merge:DefaultSourcePrecedence:1"] = MeridianConstants.SourceTypes.VendorPrescreen,
            ["Meridian:Merge:DefaultSourcePrecedence:2"] = MeridianConstants.SourceTypes.KnowledgeBase,
            ["Meridian:Merge:Policies:$.revenue:Strategy"] = "sourcePrecedence",
            ["Meridian:Merge:Policies:$.revenue:Transform"] = "normalizeToUsd",
            ["Meridian:Merge:Policies:$.productLines:Strategy"] = "collection",
            ["Meridian:Merge:Policies:$.productLines:CollectionStrategy"] = "union",
            ["Meridian:Merge:Policies:$.productLines:Transform"] = "dedupeFuzzy",
            ["Meridian:Merge:Policies:$.complianceStatus:Strategy"] = "consensus",
            ["Meridian:Merge:Policies:$.complianceStatus:ConsensusMinimumSources"] = "2"
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

        var documentStorage = new FakeDocumentStorage();
        var deliverableStorage = new FakeDeliverableStorage();
        var cache = new InMemoryEmbeddingCache();

        services.AddSingleton<IDocumentStorage>(documentStorage);
        services.AddSingleton<IDeliverableStorage>(deliverableStorage);
        services.AddSingleton<IEmbeddingCache>(cache);
        services.AddSingleton<IPdfRenderer, FakePdfRenderer>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MeridianOptions>>().Value);

        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), deliverableStorage);
    }

    private static async Task<SourceDocument> CreateSourceDocumentAsync(string pipelineId, string fileName, string sourceType, DateTime updatedAt, CancellationToken ct)
    {
        var document = new SourceDocument
        {
            PipelineId = pipelineId,
            OriginalFileName = fileName,
            StorageKey = $"fake/{Guid.NewGuid():N}",
            SourceType = sourceType,
            Status = DocumentProcessingStatus.Indexed,
            UploadedAt = updatedAt.AddMinutes(-5),
            UpdatedAt = updatedAt
        };

        return await document.Save(ct);
    }

    private static async Task<Passage> CreatePassageAsync(string pipelineId, string sourceDocumentId, string text, int page, string section, CancellationToken ct)
    {
        var passage = new Passage
        {
            PipelineId = pipelineId,
            SourceDocumentId = sourceDocumentId,
            SequenceNumber = 1,
            Text = text,
            TextHash = Encoding.UTF8.GetBytes(text).Length.ToString(),
            PageNumber = page,
            Section = section
        };

        return await passage.Save(ct);
    }

    private static ExtractedField CreateExtraction(
        string pipelineId,
        string fieldPath,
        string valueJson,
        double confidence,
        string sourceDocumentId,
        Passage passage,
        DateTime updatedAt)
    {
        return new ExtractedField
        {
            Id = Guid.NewGuid().ToString(),
            PipelineId = pipelineId,
            FieldPath = fieldPath,
            ValueJson = valueJson,
            Confidence = confidence,
            SourceDocumentId = sourceDocumentId,
            PassageId = passage.Id,
            Evidence = new TextSpanEvidence
            {
                SourceDocumentId = sourceDocumentId,
                PassageId = passage.Id,
                OriginalText = passage.Text,
                Page = passage.PageNumber,
                Section = passage.Section,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            },
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
    }

    private static ExtractedField CreateOverrideExtraction(
        string pipelineId,
        string overrideValueJson,
        string sourceDocumentId,
        Passage passage,
        DateTime updatedAt)
    {
        return new ExtractedField
        {
            Id = Guid.NewGuid().ToString(),
            PipelineId = pipelineId,
            FieldPath = "$.executiveSummary",
            ValueJson = "\"Pending\"",
            Confidence = 0.4,
            SourceDocumentId = sourceDocumentId,
            PassageId = passage.Id,
            Overridden = true,
            OverrideValueJson = overrideValueJson,
            OverrideReason = "Manual review",
            OverriddenBy = "auditor@example.com",
            OverriddenAt = updatedAt,
            Evidence = new TextSpanEvidence
            {
                SourceDocumentId = sourceDocumentId,
                PassageId = passage.Id,
                OriginalText = passage.Text,
                Page = passage.PageNumber,
                Section = passage.Section,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            },
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
    }

    private sealed class FakeDocumentStorage : IDocumentStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.OrdinalIgnoreCase);

        public async Task<string> StoreAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct).ConfigureAwait(false);
            var key = $"docs/{Guid.NewGuid():N}/{fileName}";
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
    }

    private sealed class FakeDeliverableStorage : IDeliverableStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> StoredPdfKeys => _store.Keys.ToList();

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
        private readonly ConcurrentDictionary<string, float[]> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Task<CachedEmbedding?> GetAsync(string contentHash, string modelId, string entityTypeName, CancellationToken ct = default)
        {
            var key = $"{entityTypeName}:{modelId}:{contentHash}";
            if (_entries.TryGetValue(key, out var vector))
            {
                return Task.FromResult<CachedEmbedding?>(new CachedEmbedding
                {
                    ContentHash = contentHash,
                    ModelId = modelId,
                    Embedding = vector,
                    Dimension = vector.Length,
                    CachedAt = DateTimeOffset.UtcNow
                });
            }

            return Task.FromResult<CachedEmbedding?>(null);
        }

        public Task SetAsync(string contentHash, string modelId, float[] embedding, string entityTypeName, CancellationToken ct = default)
        {
            var key = $"{entityTypeName}:{modelId}:{contentHash}";
            _entries[key] = embedding;
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
            return Task.FromResult(new CacheStats(_entries.Count, 0, null, null));
        }
    }

    private sealed class FakePdfRenderer : IPdfRenderer
    {
        public Task<byte[]> RenderAsync(string markdown, CancellationToken ct = default)
            => Task.FromResult(Encoding.UTF8.GetBytes(markdown));
    }
}
