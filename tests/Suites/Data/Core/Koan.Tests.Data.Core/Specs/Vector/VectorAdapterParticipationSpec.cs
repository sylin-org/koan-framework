using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Vector;

public sealed class VectorAdapterParticipationSpec
{
    [Fact]
    public async Task Referenced_but_unused_provider_is_visible_and_non_critical()
    {
        await using var services = Build(new TestVectorFactory("test"));
        var participation = services.GetRequiredService<IVectorAdapterParticipation>();
        var health = new TestHealthContributor("test", participation);

        health.IsCritical.Should().BeFalse();
        var report = await health.Check();

        report.State.Should().Be(HealthState.Unknown);
        report.Description.Should().Be("Vector adapter is available but not active");
        report.Data!["active"].Should().Be(false);
        health.Probes.Should().Be(0);
    }

    [Fact]
    public async Task Repository_selection_activates_exact_provider_and_source()
    {
        await using var services = Build(new TestVectorFactory("test"));
        var participation = services.GetRequiredService<IVectorAdapterParticipation>();
        var health = new TestHealthContributor("test", participation);

        services.GetRequiredService<IVectorService>()
            .TryGetRepository<ParticipationEntity, string>()
            .Should().NotBeNull();

        health.IsCritical.Should().BeTrue();
        var report = await health.Check();
        report.State.Should().Be(HealthState.Healthy);
        report.Data!["provider"].Should().Be("test");
        report.Data["sources"].Should().Be("Default");
        health.Probes.Should().Be(1);
    }

    [Fact]
    public async Task Failed_first_repository_creation_remains_an_active_health_responsibility()
    {
        await using var services = Build(new TestVectorFactory("test", failCreate: true));
        var participation = services.GetRequiredService<IVectorAdapterParticipation>();
        var health = new TestHealthContributor("test", participation) { FailProbe = true };

        var act = () => services.GetRequiredService<IVectorService>()
            .TryGetRepository<ParticipationEntity, string>();
        act.Should().Throw<InvalidOperationException>().WithMessage("factory unavailable");

        health.IsCritical.Should().BeTrue();
        var report = await health.Check();
        report.State.Should().Be(HealthState.Unhealthy);
        report.Description.Should().Be("Vector source 'Default' is unavailable");
        report.Data!["failedSource"].Should().Be("Default");
    }

    [Fact]
    public async Task Vector_only_application_does_not_require_a_record_data_provider()
    {
        await using var services = Build(new TestVectorFactory("test"));

        var repository = services.GetRequiredService<IVectorService>()
            .TryGetRepository<VectorOnlyEntity, string>();

        repository.Should().NotBeNull();
    }

    private static ServiceProvider Build(IVectorAdapterFactory factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddKoanDataCore();
        services.AddKoanDataVector();
        services.AddSingleton<IVectorAdapterFactory>(factory);
        return services.BuildServiceProvider();
    }

    private sealed class TestHealthContributor(
        string provider,
        IVectorAdapterParticipation participation)
        : VectorAdapterHealthContributorBase(provider, participation)
    {
        public int Probes { get; private set; }
        public bool FailProbe { get; init; }

        protected override Task ProbeSource(string source, CancellationToken ct)
        {
            Probes++;
            return FailProbe
                ? Task.FromException(new InvalidOperationException("probe unavailable"))
                : Task.CompletedTask;
        }
    }

    private sealed class TestVectorFactory(string provider, bool failCreate = false) : IVectorAdapterFactory
    {
        public string Provider => provider;

        public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(
            IServiceProvider services,
            string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull =>
            failCreate
                ? throw new InvalidOperationException("factory unavailable")
                : new TestVectorRepository<TEntity, TKey>();

        public Koan.Data.Abstractions.Naming.StorageNamingCapability GetNamingCapability(IServiceProvider services) =>
            new()
            {
                Style = Koan.Data.Abstractions.Naming.StorageNamingStyle.EntityType,
                PartitionSeparator = '#'
            };
    }

    private sealed class TestVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        public Task Upsert(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> UpsertMany(
            IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items,
            CancellationToken ct = default) => Task.FromResult(0);

        public Task<bool> Delete(TKey id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default) => Task.FromResult(0);

        public Task<VectorQueryResult<TKey>> Search(VectorQueryOptions options, CancellationToken ct = default) =>
            Task.FromResult(new VectorQueryResult<TKey>([], null));
    }

    [VectorAdapter("test")]
    private sealed class ParticipationEntity : Entity<ParticipationEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = "";
    }

    private sealed class VectorOnlyEntity : Entity<VectorOnlyEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = "";
    }
}
