using System.IO.Compression;
using System.Text.Json;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Core.Diagnostics;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Storage;
using Koan.Data.Core;
using Koan.Data.Core.Decorators;
using Koan.Data.Core.Infrastructure;
using Koan.Data.Core.Querying;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Koan.Data.Backup.Tests.Specs;

public sealed class StreamingBackupServiceAcceptanceSpec
{
    [Fact]
    public async Task Single_entity_backup_consumes_real_bounded_pages_and_publishes_the_complete_archive()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedFive();
            runtime.Probe.Reset();

            using var scope = runtime.Services.CreateScope();
            var manifest = await scope.ServiceProvider.GetRequiredService<IBackupService>()
                .BackupEntityAsync<BackupRecord, string>(
                    "bounded-pages",
                    new BackupOptions { BatchSize = 2, StorageProfile = Profile });

            runtime.Probe.RequestedPages.Should().Equal(1, 2, 3);
            runtime.Probe.CompletedPages.Should().SatisfyRespectively(
                page => AssertPage(page, number: 1, candidates: 2),
                page => AssertPage(page, number: 2, candidates: 2),
                page => AssertPage(page, number: 3, candidates: 1));
            runtime.Probe.CountCalls.Should().Be(0);

            manifest.Status.Should().Be(BackupStatus.Completed);
            manifest.Entities.Should().ContainSingle()
                .Which.Should().Match<EntityBackupInfo>(entity =>
                    entity.EntityType == nameof(BackupRecord)
                    && entity.Provider == "sqlite"
                    && entity.ItemCount == 5);
            manifest.Verification.TotalItemCount.Should().Be(5);
            manifest.Verification.ArchiveContentHash.Should().NotBeNullOrWhiteSpace();
            manifest.Verification.ArchiveSizeBytes.Should().BeGreaterThan(0);

            var storage = scope.ServiceProvider.GetRequiredService<BackupStorageService>();
            using var archive = await storage.OpenBackupArchive(manifest.ArchiveStorageKey, Profile);
            var archivedIds = await ReadIds(archive, manifest.Entities.Single().StorageFile);
            archivedIds.Should().Equal(ExpectedIds);

            var fact = runtime.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts.Single(item =>
                item.Code == Constants.Diagnostics.Codes.StreamExecution
                && item.Subject.EndsWith(nameof(BackupRecord), StringComparison.Ordinal));
            fact.State.Should().Be(KoanFactState.Selected);
            fact.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.ProviderBoundedPaging);
        });
    }

    [Fact]
    public async Task Explicit_partition_controls_both_source_rows_and_archive_identity()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedPartition("tenant-a", "a-0001", "a-0002");
            await SeedPartition("tenant-b", "b-0001", "b-0002", "b-0003");
            runtime.Probe.Reset();

            using var ambient = EntityContext.With(partition: "tenant-b");
            using var scope = runtime.Services.CreateScope();
            var manifest = await scope.ServiceProvider.GetRequiredService<IBackupService>()
                .BackupEntityAsync<BackupRecord, string>(
                    "partition-a",
                    new BackupOptions
                    {
                        BatchSize = 2,
                        Partition = "tenant-a",
                        StorageProfile = Profile
                    });

            runtime.Probe.ObservedPartitions.Should().NotBeEmpty()
                .And.OnlyContain(partition => partition == "tenant-a");
            var entity = manifest.Entities.Should().ContainSingle().Which;
            entity.Set.Should().Be("tenant-a");
            entity.ItemCount.Should().Be(2);

            var storage = scope.ServiceProvider.GetRequiredService<BackupStorageService>();
            using var archive = await storage.OpenBackupArchive(manifest.ArchiveStorageKey, Profile);
            (await ReadIds(archive, entity.StorageFile)).Should().Equal("a-0001", "a-0002");
        });
    }

    [Fact]
    public async Task Caller_cancellation_stops_between_real_pages_and_publishes_nothing()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedFive();
            runtime.Probe.Reset();
            using var cancellation = new CancellationTokenSource();
            runtime.Probe.CancelWhenPageIsRequested(2, cancellation);

            using var scope = runtime.Services.CreateScope();
            Func<Task> backup = () => scope.ServiceProvider.GetRequiredService<IBackupService>()
                .BackupEntityAsync<BackupRecord, string>(
                    "cancelled-between-pages",
                    new BackupOptions { BatchSize = 2, StorageProfile = Profile },
                    cancellation.Token);

            await backup.Should().ThrowAsync<OperationCanceledException>();
            runtime.Probe.RequestedPages.Should().Equal(1, 2);
            runtime.Probe.CompletedPages.Should().ContainSingle();
            AssertPage(runtime.Probe.CompletedPages.Single(), number: 1, candidates: 2);
            runtime.Probe.CountCalls.Should().Be(0);
            PublishedFiles(runtime.StorageRoot).Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData("json")]
    public async Task Unsupported_resident_adapters_reject_before_query_or_archive_publish(string provider)
    {
        await WithRuntime(provider, async runtime =>
        {
            await SeedFive();
            runtime.Probe.Reset();

            var repository = runtime.Services.GetRequiredService<IDataService>()
                .GetRepository<BackupRecord, string>();
            DataCaps.Describe(repository, provider).Has(DataCaps.Query.ProviderBoundedPaging).Should().BeFalse();

            using var scope = runtime.Services.CreateScope();
            Func<Task> backup = () => scope.ServiceProvider.GetRequiredService<IBackupService>()
                .BackupEntityAsync<BackupRecord, string>(
                    $"unsupported-{provider}",
                    new BackupOptions { BatchSize = 2, StorageProfile = Profile });

            var rejection = (await backup.Should().ThrowAsync<QueryStreamRejectedException>()).Which;
            rejection.EntityType.Should().Contain(nameof(BackupRecord));
            rejection.Provider.Should().Be(provider);
            rejection.BatchSize.Should().Be(2);
            rejection.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.MissingProviderBoundedPaging);
            rejection.Correction.Should().NotBeNullOrWhiteSpace();
            runtime.Probe.RequestedPages.Should().BeEmpty();
            runtime.Probe.CountCalls.Should().Be(0);
            PublishedFiles(runtime.StorageRoot).Should().BeEmpty();
        });
    }

    private const string Profile = "acceptance";
    private static readonly string[] ExpectedIds = ["0001", "0002", "0003", "0004", "0005"];

    private static async Task SeedFive()
    {
        await BackupRecord.UpsertMany(ExpectedIds.Select((id, index) => new BackupRecord
        {
            Id = id,
            Sequence = index + 1
        }));
    }

    private static async Task SeedPartition(string partition, params string[] ids)
    {
        using var scope = EntityContext.With(partition: partition);
        await BackupRecord.UpsertMany(ids.Select((id, index) => new BackupRecord
        {
            Id = id,
            Sequence = index + 1
        }));
    }

    private static async Task WithRuntime(string provider, Func<Runtime, Task> assertion)
    {
        var root = Path.Combine(Path.GetTempPath(), "koan-backup-stream", Guid.CreateVersion7().ToString("n"));
        var storageRoot = Path.Combine(root, "storage");
        Directory.CreateDirectory(root);
        var probe = new BackupStreamProbe();
        IntegrationHost? host = null;

        try
        {
            host = await KoanIntegrationHost.Configure()
                .WithEnvironment("Test")
                .WithSetting("Koan:Orchestration:ForceOrchestrationMode", "Standalone")
                .WithSetting("Koan:Data:Sources:Default:Adapter", provider)
                .WithSetting("Koan:Data:Sqlite:ConnectionString", $"Data Source={Path.Combine(root, "backup.db")}")
                .WithSetting("Koan:Data:Json:DirectoryPath", Path.Combine(root, "json"))
                .WithSetting("Koan:Storage:Providers:Local:BasePath", storageRoot)
                .WithSetting("Koan:Storage:DefaultProfile", Profile)
                .WithSetting($"Koan:Storage:Profiles:{Profile}:Provider", "local")
                .WithSetting($"Koan:Storage:Profiles:{Profile}:Container", "backups")
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddSingleton<IDataRepositoryDecorator>(new BackupProbeDecorator(probe));
                })
                .StartAsync(TestContext.Current.CancellationToken);

            AppHost.Current = host.Services;
            TestHooks.ResetDataConfigs();
            await assertion(new Runtime(host.Services, storageRoot, probe));
        }
        finally
        {
            if (host is not null)
            {
                if (ReferenceEquals(AppHost.Current, host.Services)) AppHost.Current = null;
                TestHooks.ResetDataConfigs();
                await host.DisposeAsync();
            }

            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup for a failed assertion */ }
        }
    }

    private static void AssertPage(PageObservation page, int number, int candidates)
    {
        page.Number.Should().Be(number);
        page.Size.Should().Be(2);
        page.Candidates.Should().Be(candidates);
        page.PaginationHandled.Should().BeTrue();
        page.SortHandled.Should().BeTrue();
        page.CountRequested.Should().BeFalse();
    }

    private static async Task<IReadOnlyList<string>> ReadIds(ZipArchive archive, string storageFile)
    {
        var entry = archive.GetEntry(storageFile);
        entry.Should().NotBeNull();
        await using var stream = entry!.Open();
        using var reader = new StreamReader(stream);
        var ids = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            ids.Add(document.RootElement.GetProperty(nameof(BackupRecord.Id)).GetString()!);
        }

        return ids;
    }

    private static IReadOnlyList<string> PublishedFiles(string storageRoot)
        => Directory.Exists(storageRoot)
            ? Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories).ToList()
            : [];

    private sealed record Runtime(IServiceProvider Services, string StorageRoot, BackupStreamProbe Probe);
    private sealed record PageObservation(
        int Number,
        int Size,
        int Candidates,
        bool PaginationHandled,
        bool SortHandled,
        bool CountRequested);

    private sealed class BackupRecord : Entity<BackupRecord>
    {
        public int Sequence { get; set; }
    }

    private sealed class BackupStreamProbe
    {
        private int? _cancelPage;
        private CancellationTokenSource? _cancellation;

        public List<int> RequestedPages { get; } = [];
        public List<PageObservation> CompletedPages { get; } = [];
        public List<string?> ObservedPartitions { get; } = [];
        public int CountCalls { get; private set; }

        public void Reset()
        {
            RequestedPages.Clear();
            CompletedPages.Clear();
            ObservedPartitions.Clear();
            CountCalls = 0;
            _cancelPage = null;
            _cancellation = null;
        }

        public void CancelWhenPageIsRequested(int page, CancellationTokenSource cancellation)
        {
            _cancelPage = page;
            _cancellation = cancellation;
        }

        public void Requested(QueryDefinition query, CancellationToken ct)
        {
            var page = query.EffectivePage();
            RequestedPages.Add(page);
            ObservedPartitions.Add(EntityContext.Current?.Partition);
            if (_cancelPage == page)
            {
                _cancellation!.Cancel();
                ct.ThrowIfCancellationRequested();
            }
        }

        public void Completed(QueryDefinition query, RepositoryQueryResult<BackupRecord> result)
            => CompletedPages.Add(new PageObservation(
                query.EffectivePage(),
                query.EffectivePageSize(),
                result.Items.Count,
                result.PaginationHandled,
                result.SortFullyHandled(query),
                query.CountStrategy is not null));

        public void Counted() => CountCalls++;
    }

    private sealed class BackupProbeDecorator(BackupStreamProbe probe) : IDataRepositoryDecorator
    {
        public object? TryDecorate(Type entityType, Type keyType, object repository, IServiceProvider services)
            => entityType == typeof(BackupRecord) && keyType == typeof(string)
                ? new ProbeRepository((IDataRepository<BackupRecord, string>)repository, probe)
                : null;
    }

    private sealed class ProbeRepository :
        IDataRepository<BackupRecord, string>,
        IQueryRepository<BackupRecord, string>,
        IDescribesCapabilities
    {
        private readonly IDataRepository<BackupRecord, string> _inner;
        private readonly IQueryRepository<BackupRecord, string> _query;
        private readonly BackupStreamProbe _probe;

        public ProbeRepository(IDataRepository<BackupRecord, string> inner, BackupStreamProbe probe)
        {
            _inner = inner;
            _query = inner as IQueryRepository<BackupRecord, string>
                ?? throw new InvalidOperationException("The backup acceptance repository must support structured queries.");
            _probe = probe;
        }

        public void Describe(ICapabilities capabilities)
            => DataCaps.Describe(_inner, _inner.GetType().Name).CopyInto(capabilities);

        public async Task<RepositoryQueryResult<BackupRecord>> Query(QueryDefinition query, CancellationToken ct = default)
        {
            _probe.Requested(query, ct);
            var result = await _query.Query(query, ct);
            _probe.Completed(query, result);
            return result;
        }

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
        {
            _probe.Counted();
            return _query.Count(query, ct);
        }

        public Task EnsureReady(CancellationToken ct = default) => _inner.EnsureReady(ct);
        public Task<BackupRecord?> Get(string id, CancellationToken ct = default) => _inner.Get(id, ct);
        public Task<IReadOnlyList<BackupRecord?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default) => _inner.GetMany(ids, ct);
        public Task<BackupRecord> Upsert(BackupRecord model, CancellationToken ct = default) => _inner.Upsert(model, ct);
        public Task<int> UpsertMany(IEnumerable<BackupRecord> models, CancellationToken ct = default) => _inner.UpsertMany(models, ct);
        public Task<bool> Delete(string id, CancellationToken ct = default) => _inner.Delete(id, ct);
        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default) => _inner.DeleteMany(ids, ct);
        public Task<int> DeleteAll(CancellationToken ct = default) => _inner.DeleteAll(ct);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => _inner.RemoveAll(strategy, ct);
        public IBatchSet<BackupRecord, string> CreateBatch() => _inner.CreateBatch();
    }
}
