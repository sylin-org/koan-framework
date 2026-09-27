using Koan.Core.Composition;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Connector.InMemory;
using Koan.Data.Connector.Json;
using Koan.Data.Connector.Sqlite;
using Koan.Data.Core.Options;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Data.Core.Specs.Setup;

public sealed class DataProviderSetupSpec
{
    [Fact]
    public async Task Catalog_projection_uses_canonical_identity_and_alias_probe_without_mutating_sources()
    {
        var factory = new SetupFactory();
        var catalog = new DataProviderCatalog([factory], references: null);
        var sources = new DataSourceRegistry();
        var setup = Create(catalog);

        setup.Candidates.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = "sample",
            DisplayName = "Sample store",
            Aliases = new[] { "sample-alias" },
            SupportsProbe = true,
            Capabilities = Array.Empty<string>()
        });

        var result = await setup.Probe("SAMPLE-ALIAS", new Dictionary<string, string?>
        {
            ["endpoint"] = "candidate"
        });

        result.Status.Should().Be(DataProviderProbeStatus.Ready);
        factory.LastCandidate!.Get("Endpoint").Should().Be("candidate");
        factory.LastCandidate.Get("Database").Should().Be("Koan");
        sources.GetSource("Default").Should().BeNull();
    }

    [Fact]
    public async Task Candidate_validation_rejects_missing_and_unknown_fields_before_adapter_execution()
    {
        var factory = new SetupFactory();
        var setup = Create(new DataProviderCatalog([factory], references: null));

        var missing = await setup.Probe("sample", new Dictionary<string, string?>());
        var unknown = await setup.Probe("sample", new Dictionary<string, string?>
        {
            ["Endpoint"] = "candidate",
            ["SecretTypo"] = "must-not-be-echoed"
        });

        missing.Status.Should().Be(DataProviderProbeStatus.InvalidConfiguration);
        unknown.Status.Should().Be(DataProviderProbeStatus.InvalidConfiguration);
        unknown.Message.Should().NotContain("must-not-be-echoed");
        factory.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Coordinator_distinguishes_timeout_and_preserves_caller_cancellation()
    {
        var factory = new SetupFactory(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return DataProviderProbeResult.Ready();
        });
        var setup = Create(new DataProviderCatalog([factory], references: null), TimeSpan.FromMilliseconds(25));

        var timedOut = await setup.Probe("sample", new Dictionary<string, string?> { ["Endpoint"] = "candidate" });
        timedOut.Status.Should().Be(DataProviderProbeStatus.TimedOut);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var act = () => setup.Probe(
            "sample",
            new Dictionary<string, string?> { ["Endpoint"] = "candidate" },
            cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Bundled_local_adapters_expose_truthful_non_creating_probes()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var memory = new InMemoryAdapterFactory();
        var sqlite = new SqliteAdapterFactory();
        var json = new JsonAdapterFactory();
        var candidates = Create(new DataProviderCatalog([memory, sqlite, json], references: null)).Candidates;
        candidates.Single(candidate => candidate.Id == "inmemory").Capabilities
            .Should().NotContain(DataCaps.Persistency.Id);
        candidates.Single(candidate => candidate.Id == "sqlite").Capabilities
            .Should().Contain(DataCaps.Persistency.Id);
        candidates.Single(candidate => candidate.Id == "json").Capabilities
            .Should().Contain(DataCaps.Persistency.Id);
        var directory = Path.Combine(Path.GetTempPath(), $"koan-provider-setup-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(directory);
        try
        {
            (await memory.Probe(services, new DataProviderProbeContext([]))).Status
                .Should().Be(DataProviderProbeStatus.Ready);
            (await sqlite.Probe(services, new DataProviderProbeContext(new Dictionary<string, string?>
            {
                ["ConnectionString"] = "Data Source=:memory:"
            }))).Status.Should().Be(DataProviderProbeStatus.Ready);
            (await json.Probe(services, new DataProviderProbeContext(new Dictionary<string, string?>
            {
                ["DirectoryPath"] = directory
            }))).Status.Should().Be(DataProviderProbeStatus.Ready);
            Directory.EnumerateFileSystemEntries(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DataProviderSetup Create(DataProviderCatalog catalog, TimeSpan? timeout = null) =>
        new(
            new ServiceCollection().BuildServiceProvider(),
            catalog,
            Options.Create(new SourceIntegrationOptions
            {
                DoctorTimeout = timeout ?? TimeSpan.FromSeconds(1)
            }));

    private sealed class SetupFactory(
        Func<DataProviderProbeContext, CancellationToken, Task<DataProviderProbeResult>>? probe = null)
        : IDataAdapterFactory, IDataAdapterSetup
    {
        public string Provider => "sample";
        public IReadOnlyCollection<string> Aliases => ["sample-alias"];
        public IReadOnlyCollection<string> ReferenceIdentities => [];
        public int ProbeCount { get; private set; }
        public DataProviderProbeContext? LastCandidate { get; private set; }

        public DataAdapterSetupDescriptor DescribeSetup() => new("Sample store",
        [
            new("Endpoint", "Endpoint", DataProviderSetupFieldKind.ConnectionString),
            new("Database", "Database", DataProviderSetupFieldKind.Text, Required: false, DefaultValue: "Koan")
        ]);

        public Task<DataProviderProbeResult> Probe(
            IServiceProvider services, DataProviderProbeContext candidate, CancellationToken ct = default)
        {
            ProbeCount++;
            LastCandidate = candidate;
            return probe?.Invoke(candidate, ct) ?? Task.FromResult(DataProviderProbeResult.Ready());
        }

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull => throw new NotSupportedException();

        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
    }
}
