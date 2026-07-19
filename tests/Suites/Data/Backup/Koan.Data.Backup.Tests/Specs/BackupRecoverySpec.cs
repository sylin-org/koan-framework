using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Core.Diagnostics;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Core;
using Koan.Data.Core.Decorators;
using Koan.Data.Core.Infrastructure;
using Koan.Data.Core.Querying;
using Koan.Storage.Abstractions;
using Koan.Storage.Keys;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Koan.Data.Backup.Tests.Specs;

public sealed class BackupRecoverySpec
{
    [Fact]
    public async Task Create_consumes_provider_bounded_pages_and_publishes_one_complete_archive()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedFive();
            runtime.Probe.Reset();

            using var scope = runtime.Services.CreateScope();
            var receipt = await scope.ServiceProvider.GetRequiredService<IBackupService>()
                .Create<BackupRecord, string>(
                    "bounded-pages",
                    new BackupRequest { PageSize = 2, StorageProfile = Profile });

            runtime.Probe.RequestedPages.Should().Equal(1, 2, 3);
            runtime.Probe.CompletedPages.Should().SatisfyRespectively(
                page => AssertPage(page, number: 1, candidates: 2),
                page => AssertPage(page, number: 2, candidates: 2),
                page => AssertPage(page, number: 3, candidates: 1));
            runtime.Probe.CountCalls.Should().Be(0);

            receipt.RecordCount.Should().Be(5);
            receipt.DataContentSha256.Should().HaveLength(64);
            receipt.ArchiveBytes.Should().BeGreaterThan(0);
            receipt.StorageKey.Should().EndWith(".zip");
            (await ReadIds(scope.ServiceProvider, receipt)).Should().Equal(ExpectedIds);

            var fact = runtime.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts.Single(item =>
                item.Code == Constants.Diagnostics.Codes.StreamExecution
                && item.Subject.EndsWith(nameof(BackupRecord), StringComparison.Ordinal));
            fact.State.Should().Be(KoanFactState.Selected);
            fact.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.ProviderBoundedPaging);
        });
    }

    [Fact]
    public async Task Create_and_restore_round_trip_preserves_partition_and_records()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedPartition("tenant-a", "a-0001", "a-0002");
            await SeedPartition("tenant-b", "b-0001");

            using var scope = runtime.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var backup = await service.Create<BackupRecord, string>(
                "tenant-a",
                new BackupRequest { PageSize = 1, Partition = "tenant-a", StorageProfile = Profile });

            using (EntityContext.With(partition: "tenant-a"))
                (await Data<BackupRecord, string>.DeleteAll()).Should().Be(2);

            var restore = await service.Restore<BackupRecord, string>(
                backup.StorageKey,
                new RestoreRequest { BatchSize = 1, StorageProfile = Profile });

            restore.ArchiveId.Should().Be(backup.ArchiveId);
            restore.RecordCount.Should().Be(2);
            restore.TargetPartition.Should().Be("tenant-a");
            restore.DataContentSha256.Should().Be(backup.DataContentSha256);

            using (EntityContext.With(partition: "tenant-a"))
                (await BackupRecord.All()).Select(x => x.Id).Should().Equal("a-0001", "a-0002");
            using (EntityContext.With(partition: "tenant-b"))
                (await BackupRecord.All()).Select(x => x.Id).Should().Equal("b-0001");
        });
    }

    [Fact]
    public async Task Repeated_names_publish_distinct_archives()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedFive();
            using var scope = runtime.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();

            var first = await service.Create<BackupRecord, string>(
                "same-business-name",
                new BackupRequest { StorageProfile = Profile });
            var second = await service.Create<BackupRecord, string>(
                "same-business-name",
                new BackupRequest { StorageProfile = Profile });

            first.ArchiveId.Should().NotBe(second.ArchiveId);
            first.StorageKey.Should().NotBe(second.StorageKey);
            PublishedArchives(runtime.StorageRoot).Should().HaveCount(2);
        });
    }

    [Fact]
    public async Task Malformed_archive_fails_before_the_first_restore_mutation()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedFive();
            using var scope = runtime.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var backup = await service.Create<BackupRecord, string>(
                "corruption-drill",
                new BackupRequest { PageSize = 2, StorageProfile = Profile });

            CorruptDataEntry(runtime.StorageRoot);
            await SeedPartition("recovery-target", "sentinel");

            Func<Task> restore = () => service.Restore<BackupRecord, string>(
                backup.StorageKey,
                new RestoreRequest
                {
                    BatchSize = 1,
                    StorageProfile = Profile,
                    TargetPartition = "recovery-target"
                });

            await restore.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*record*malformed*");
            using (EntityContext.With(partition: "recovery-target"))
                (await BackupRecord.All()).Select(x => x.Id).Should().Equal("sentinel");
        });
    }

    [Fact]
    public async Task Type_mismatch_fails_before_the_first_restore_mutation()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedFive();
            using var scope = runtime.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var backup = await service.Create<BackupRecord, string>(
                "type-drill",
                new BackupRequest { StorageProfile = Profile });

            Func<Task> restore = () => service.Restore<OtherRecord, string>(
                backup.StorageKey,
                new RestoreRequest { StorageProfile = Profile });

            await restore.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*not*");
            (await OtherRecord.All()).Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Caller_cancellation_stops_between_pages_and_publishes_nothing()
    {
        await WithRuntime("sqlite", async runtime =>
        {
            await SeedFive();
            runtime.Probe.Reset();
            using var cancellation = new CancellationTokenSource();
            runtime.Probe.CancelWhenPageIsRequested(2, cancellation);

            using var scope = runtime.Services.CreateScope();
            Func<Task> backup = () => scope.ServiceProvider.GetRequiredService<IBackupService>()
                .Create<BackupRecord, string>(
                    "cancelled-between-pages",
                    new BackupRequest { PageSize = 2, StorageProfile = Profile },
                    cancellation.Token);

            await backup.Should().ThrowAsync<OperationCanceledException>();
            runtime.Probe.RequestedPages.Should().Equal(1, 2);
            PublishedArchives(runtime.StorageRoot).Should().BeEmpty();
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
                .Create<BackupRecord, string>(
                    $"unsupported-{provider}",
                    new BackupRequest { PageSize = 2, StorageProfile = Profile });

            var rejection = (await backup.Should().ThrowAsync<QueryStreamRejectedException>()).Which;
            rejection.EntityType.Should().Contain(nameof(BackupRecord));
            rejection.Provider.Should().Be(provider);
            runtime.Probe.RequestedPages.Should().BeEmpty();
            runtime.Probe.CountCalls.Should().Be(0);
            PublishedArchives(runtime.StorageRoot).Should().BeEmpty();
        });
    }

    private const string Profile = "acceptance";
    private const string DataEntry = "entity.jsonl";
    private static readonly string[] ExpectedIds = ["0001", "0002", "0003", "0004", "0005"];

    private static async Task SeedFive()
        => await BackupRecord.UpsertMany(ExpectedIds.Select((id, index) => new BackupRecord { Id = id, Sequence = index + 1 }));

    private static async Task SeedPartition(string partition, params string[] ids)
    {
        using var scope = EntityContext.With(partition: partition);
        await BackupRecord.UpsertMany(ids.Select((id, index) => new BackupRecord { Id = id, Sequence = index + 1 }));
    }

    private static async Task WithRuntime(string provider, Func<Runtime, Task> assertion)
    {
        var root = Path.Combine(Path.GetTempPath(), "koan-backup-recovery", Guid.CreateVersion7().ToString("n"));
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
            catch { }
        }
    }

    private static async Task<IReadOnlyList<string>> ReadIds(IServiceProvider services, BackupReceipt receipt)
    {
        using var scope = StorageScope.HostScoped();
        await using var stream = await services.GetRequiredService<IStorageService>()
            .Read(receipt.StorageProfile, "backups", receipt.StorageKey);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(DataEntry);
        entry.Should().NotBeNull();
        await using var input = entry!.Open();
        using var reader = new StreamReader(input);
        var ids = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            ids.Add(document.RootElement.GetProperty("id").GetString()!);
        }
        return ids;
    }

    private static void CorruptDataEntry(string storageRoot)
    {
        var path = PublishedArchives(storageRoot).Should().ContainSingle().Which;
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = archive.GetEntry(DataEntry);
        entry.Should().NotBeNull();
        string first;
        using (var reader = new StreamReader(entry!.Open(), Encoding.UTF8))
            first = reader.ReadLine()!;
        entry.Delete();
        var replacement = archive.CreateEntry(DataEntry);
        using var writer = new StreamWriter(replacement.Open(), Encoding.UTF8);
        writer.WriteLine(first);
        writer.WriteLine("{not-json}");
    }

    private static IReadOnlyList<string> PublishedArchives(string storageRoot)
        => Directory.Exists(storageRoot)
            ? Directory.EnumerateFiles(storageRoot, "*.zip", SearchOption.AllDirectories).ToList()
            : [];

    private static void AssertPage(PageObservation page, int number, int candidates)
    {
        page.Number.Should().Be(number);
        page.Size.Should().Be(2);
        page.Candidates.Should().Be(candidates);
        page.PaginationHandled.Should().BeTrue();
        page.SortHandled.Should().BeTrue();
        page.CountRequested.Should().BeFalse();
    }

    private sealed record Runtime(IServiceProvider Services, string StorageRoot, BackupStreamProbe Probe);
    private sealed record PageObservation(int Number, int Size, int Candidates, bool PaginationHandled, bool SortHandled, bool CountRequested);

    private sealed class BackupRecord : Entity<BackupRecord>
    {
        public int Sequence { get; set; }
    }

    private sealed class OtherRecord : Entity<OtherRecord>;

    private sealed class BackupStreamProbe
    {
        private int? _cancelPage;
        private CancellationTokenSource? _cancellation;
        public List<int> RequestedPages { get; } = [];
        public List<PageObservation> CompletedPages { get; } = [];
        public int CountCalls { get; private set; }

        public void Reset()
        {
            RequestedPages.Clear();
            CompletedPages.Clear();
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
            if (_cancelPage == page)
            {
                _cancellation!.Cancel();
                ct.ThrowIfCancellationRequested();
            }
        }

        public void Completed(QueryDefinition query, RepositoryQueryResult<BackupRecord> result)
            => CompletedPages.Add(new PageObservation(
                query.EffectivePage(), query.EffectivePageSize(), result.Items.Count,
                result.PaginationHandled, result.SortFullyHandled(query), query.CountStrategy is not null));

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

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default) { _probe.Counted(); return _query.Count(query, ct); }
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
