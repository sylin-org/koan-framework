using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using S13.DocMind.Contracts;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.IntegrationTests;

public sealed class DocMindTestHarness : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _workDir;

    private DocMindTestHarness(
        IHost host,
        string workDir,
        TimeProvider clock,
        FakeDiscoveryRefreshScheduler scheduler,
        FakeDocumentStorage storage)
    {
        _host = host;
        _workDir = workDir;
        Clock = clock;
        RefreshScheduler = scheduler;
        Storage = storage;
    }

    public IServiceProvider Services => _host.Services;

    public TimeProvider Clock { get; }

    public FakeDiscoveryRefreshScheduler RefreshScheduler { get; }

    public FakeDocumentStorage Storage { get; }

    public static async Task<DocMindTestHarness> StartAsync()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "docmind-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        Koan.Data.Core.TestHooks.ResetDataConfigs();

        var configValues = new Dictionary<string, string?>
        {
            ["Koan_DATA_PROVIDER"] = "json",
            ["Koan:Data:Sources:Default:json:DirectoryPath"] = workDir,
            [$"{DocMindOptions.Section}:Storage:BasePath"] = workDir,
            [$"{DocMindOptions.Section}:Storage:Bucket"] = "integration-tests",
            [$"{DocMindOptions.Section}:Processing:PollIntervalSeconds"] = "1",
            [$"{DocMindOptions.Section}:Processing:WorkerBatchSize"] = "8",
            [$"{DocMindOptions.Section}:Processing:EnableVisionExtraction"] = "false",
            [$"{DocMindOptions.Section}:Ai:DefaultModel"] = "fake-model",
            [$"{DocMindOptions.Section}:Ai:EmbeddingModel"] = "fake-embed"
        };

        var host = new HostBuilder()
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(configValues))
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddOptions();
                services.AddKoanDataCore();
                services.AddSingleton(TimeProvider.System);
                services.Configure<DocMindOptions>(context.Configuration.GetSection(DocMindOptions.Section));

                services.AddSingleton<FakeDocumentStorage>();
                services.AddSingleton<IDocumentStorage>(sp => sp.GetRequiredService<FakeDocumentStorage>());

                services.AddScoped<IDocumentProcessingEventSink, DocumentProcessingEventRepositorySink>();
                services.AddScoped<IDocumentIntakeService, DocumentIntakeService>();
                services.AddScoped<ITextExtractionService, FakeTextExtractionService>();
                services.AddScoped<IEmbeddingGenerator, FakeEmbeddingGenerator>();
                services.AddScoped<IVisionInsightService, FakeVisionInsightService>();
                services.AddScoped<IInsightSynthesisService, FakeInsightSynthesisService>();
                services.AddScoped<ITemplateSuggestionService, FakeTemplateSuggestionService>();

                services.AddSingleton<FakeDiscoveryRefreshScheduler>();
                services.AddSingleton<IDocumentDiscoveryRefreshScheduler>(sp => sp.GetRequiredService<FakeDiscoveryRefreshScheduler>());
                services.AddSingleton<DocMindVectorHealth>();
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);

        Koan.Core.Hosting.App.AppHost.Current = host.Services;

        var clock = host.Services.GetRequiredService<TimeProvider>();
        var scheduler = host.Services.GetRequiredService<FakeDiscoveryRefreshScheduler>();
        var storage = host.Services.GetRequiredService<FakeDocumentStorage>();

        return new DocMindTestHarness(host, workDir, clock, scheduler, storage);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Koan.Core.Hosting.App.AppHost.Current = null;
        }
        catch
        {
        }

        try
        {
            await _host.StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        _host.Dispose();

        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
        }

        Koan.Data.Core.TestHooks.ResetDataConfigs();
    }
}

public sealed class FakeDocumentStorage : IDocumentStorage
{
    private readonly string _storageRoot;
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public FakeDocumentStorage(IOptions<DocMindOptions> options)
    {
        var basePath = options.Value.Storage.BasePath;
        _storageRoot = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(AppContext.BaseDirectory, basePath);
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<StoredDocumentDescriptor> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        var key = $"{Guid.NewGuid():N}-{Path.GetFileName(fileName)}";
        var path = Path.Combine(_storageRoot, key);

        await using (var buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var data = buffer.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA512.HashData(data)).ToLowerInvariant();
            _paths[key] = path;

            return new StoredDocumentDescriptor
            {
                Provider = "fake",
                Bucket = "fake",
                ObjectKey = key,
                ProviderPath = path,
                Length = data.LongLength,
                Hash = hash
            };
        }
    }

    public Task<Stream> OpenReadAsync(DocumentStorageLocation location, CancellationToken cancellationToken)
    {
        if (!_paths.TryGetValue(location.ObjectKey, out var path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Stored document not found", location.ObjectKey);
        }

        Stream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(DocumentStorageLocation location, CancellationToken cancellationToken)
        => Task.FromResult(_paths.TryGetValue(location.ObjectKey, out var path) && File.Exists(path));

    public Task<bool> TryDeleteAsync(DocumentStorageLocation location, CancellationToken cancellationToken)
    {
        if (_paths.TryGetValue(location.ObjectKey, out var path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
                _paths.TryRemove(location.ObjectKey, out _);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(false);
    }
}

internal sealed class FakeTextExtractionService : ITextExtractionService
{
    public Task<DocumentExtractionResult> ExtractAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        var text = $"Harness processed {document.DisplayName ?? document.FileName}.";
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "fake"
        };

        var chunks = new List<ExtractedChunk>
        {
            new(0, text, summary: null, new Dictionary<string, object?>())
        };

        var result = new DocumentExtractionResult(
            text,
            chunks,
            WordCount: text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length,
            PageCount: 1,
            ContainsImages: false,
            Diagnostics: diagnostics,
            Language: "en");

        return Task.FromResult(result);
    }
}

internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator
{
    public Task<EmbeddingGenerationResult> GenerateAsync(string text, CancellationToken cancellationToken)
        => Task.FromResult(new EmbeddingGenerationResult(Array.Empty<float>(), TimeSpan.FromMilliseconds(5), "fake-embed"));
}

internal sealed class FakeVisionInsightService : IVisionInsightService
{
    public Task<VisionInsightResult?> TryExtractAsync(SourceDocument document, CancellationToken ct = default)
        => Task.FromResult<VisionInsightResult?>(null);
}

public sealed class FakeInsightSynthesisService : IInsightSynthesisService
{
    public Task<InsightSynthesisResult> GenerateAsync(SourceDocument document, DocumentExtractionResult extraction, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var documentId = Guid.Parse(document.Id);
        var insight = new DocumentInsight
        {
            SourceDocumentId = documentId,
            Channel = InsightChannel.Text,
            Heading = "Harness summary",
            Body = extraction.Text,
            Confidence = 0.9,
            Section = "summary",
            StructuredPayload = new Dictionary<string, object?>
            {
                ["source"] = "harness"
            },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = "harness"
            }
        };

        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["insights.total"] = 1,
            ["chunks.total"] = chunks.Count
        };

        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = "harness"
        };

        return Task.FromResult(new InsightSynthesisResult(new[] { insight }, metrics, context, 12, 6));
    }

    public Task<ManualAnalysisSynthesisResult> GenerateManualSessionAsync(ManualAnalysisSession session, SemanticTypeProfile? profile, IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken)
    {
        var synthesis = new ManualAnalysisSynthesis
        {
            SessionId = Guid.Parse(session.Id),
            Findings = "Fake harness findings",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = "harness"
            }
        };

        var runTelemetry = new ManualAnalysisRun
        {
            SessionId = Guid.Parse(session.Id),
            Status = "completed",
            DurationMs = 100
        };

        return Task.FromResult(new ManualAnalysisSynthesisResult(synthesis, runTelemetry));
    }
}

public sealed class FakeTemplateSuggestionService : ITemplateSuggestionService
{
    public Task<SemanticTypeProfile> GenerateAsync(TemplateGenerationRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new SemanticTypeProfile());

    public Task<TemplatePromptTestResult> RunPromptTestAsync(SemanticTypeProfile profile, TemplatePromptTestRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new TemplatePromptTestResult());

    public Task<IReadOnlyList<DocumentProfileSuggestion>> SuggestAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var suggestion = new DocumentProfileSuggestion
        {
            ProfileId = "profile-harness",
            Confidence = 0.75,
            SuggestedAt = DateTimeOffset.UtcNow,
            AutoAccepted = true,
            Diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = "fake",
                ["category"] = "tests"
            }
        };

        return Task.FromResult<IReadOnlyList<DocumentProfileSuggestion>>(new[] { suggestion });
    }
}

public sealed class FakeDiscoveryRefreshScheduler : IDocumentDiscoveryRefreshScheduler
{
    private int _refreshCount;

    public int RefreshCount => _refreshCount;

    public Task EnsureRefreshAsync(string reason, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refreshCount);
        return Task.CompletedTask;
    }

    public DocumentDiscoveryRefreshStatus Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new DocumentDiscoveryRefreshStatus(
            PendingCount: 0,
            LastCompletedAt: now,
            LastDuration: TimeSpan.FromMilliseconds(5),
            LastError: null,
            LastQueuedAt: now,
            LastReason: "harness",
            LastStartedAt: now,
            TotalCompleted: _refreshCount,
            TotalFailed: 0,
            AverageDuration: TimeSpan.FromMilliseconds(5),
            MaxDuration: TimeSpan.FromMilliseconds(5));
    }
}
