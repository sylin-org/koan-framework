using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonCompositionSpec
{
    [Fact]
    public async Task Runtime_uses_the_persistence_registered_with_standard_DI()
    {
        var persistence = new PlanPersistence();
        var plan = CanonCompositionCompiler.Compile([typeof(ContributorFreeCanon)], []);
        var registrations = new ServiceCollection()
            .AddSingleton(plan)
            .AddSingleton<ICanonPersistence>(persistence);
        registrations.AddCanonRuntime();

        await using var services = registrations.BuildServiceProvider();
        var runtime = services.GetRequiredService<ICanonRuntime>();

        await runtime.Canonize(new ContributorFreeCanon { Email = "di@example.com" });

        persistence.Canonicals.Should().ContainSingle();
        persistence.Indexes.Should().ContainSingle();
    }

    [Fact]
    public async Task Contributor_free_model_receives_default_pipeline_and_converges()
    {
        var plan = CanonCompositionCompiler.Compile([typeof(ContributorFreeCanon)], []);
        plan.Models.Should().ContainSingle();
        plan.Models[0].HasCustomContributors.Should().BeFalse();

        await using var services = new ServiceCollection().BuildServiceProvider();
        var persistence = new PlanPersistence();
        var builder = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(new NoopAuditSink());

        CanonCompositionCompiler.Configure(builder, services, plan);
        var configuration = builder.BuildConfiguration();
        configuration.PipelineMetadata.Should().ContainKey(typeof(ContributorFreeCanon));
        configuration.PipelineMetadata[typeof(ContributorFreeCanon)].Phases
            .Should().Contain(CanonPipelinePhase.Aggregation);

        var runtime = builder.Build();
        var first = await runtime.Canonize(new ContributorFreeCanon { Email = "same@example.com" });
        var second = await runtime.Canonize(new ContributorFreeCanon { Email = "same@example.com" });

        second.Canonical.Id.Should().Be(first.Canonical.Id);
        persistence.Indexes.Should().ContainSingle();
        persistence.Canonicals.Should().ContainSingle();
    }

    private sealed class ContributorFreeCanon : CanonEntity<ContributorFreeCanon>
    {
        [AggregationKey]
        public string Email { get; set; } = "";
    }

    private sealed class PlanPersistence : ICanonPersistence
    {
        private readonly Dictionary<string, CanonIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _canonicals = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<CanonIndex> Indexes => _indexes.Values;
        public IReadOnlyCollection<object> Canonicals => _canonicals.Values;

        public Task<TModel?> GetCanonicalAsync<TModel>(string canonicalId, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(_canonicals.TryGetValue(canonicalId, out var entity) ? (TModel?)entity : null);

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            _canonicals[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(stage);

        public Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult(_indexes.TryGetValue(Key(entityType, key), out var index) ? index : null);

        public Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken)
        {
            _indexes[Key(index.EntityType, index.Key)] = index;
            return Task.CompletedTask;
        }

        private static string Key(string entityType, string key) => $"{entityType}::{key}";
    }

    private sealed class NoopAuditSink : ICanonAuditSink
    {
        public Task Write(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
