using System.Threading;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeSpec
{
    [Fact]
    public async Task Canonize_persists_entity_and_records_index()
    {
        var (persistence, runtime) = BuildRuntime();

        var email = $"canon-{Guid.CreateVersion7():N}@example.com";

        var entity = new ContactCanon
        {
            Email = email,
            PhoneNumber = "555-0100",
            DisplayName = "Ada Lovelace"
        };

        var options = CanonizationOptions.Default
            .WithOrigin("unit-spec")
            .WithTag("tracking", "enabled");

        var result = await runtime.Canonize(entity, options, CancellationToken.None);

        var entityType = typeof(ContactCanon).FullName ?? typeof(ContactCanon).Name;
        var indexKey = $"Email={email}";

        result.Outcome.Should().Be(CanonizationOutcome.Canonized);
        result.DistributionSkipped.Should().BeTrue();
        result.Canonical.DisplayName.Should().Be("ADA LOVELACE");
        result.Metadata.CanonicalId.Should().NotBeNullOrWhiteSpace();
        result.Metadata.Tags.Should().ContainKey("processed").WhoseValue.Should().Be("true");
        result.Metadata.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");
        result.Events.Should().NotBeEmpty();
        result.Events.Select(evt => evt.Phase).Should().Contain(CanonPipelinePhase.Aggregation);

        persistence.CanonicalEntities.OfType<ContactCanon>()
            .Should().ContainSingle(c => c.Id == result.Canonical.Id);

        var index = persistence.FindIndex(entityType, indexKey);
        index.Should().NotBeNull();
        index!.CanonicalId.Should().Be(result.Canonical.Id);
        index.Attributes.Should().ContainKey("arrivalToken").WhoseValue.Should().Be(result.Canonical.Id);
    }

    [Fact]
    public async Task Stage_only_requests_create_stage_record()
    {
        var (persistence, runtime) = BuildRuntime();

        var email = $"stage-{Guid.CreateVersion7():N}@example.com";
        var correlation = $"corr-{Guid.CreateVersion7():N}";

        var entity = new ContactCanon
        {
            Email = email,
            PhoneNumber = "555-0200",
            DisplayName = "Grace Hopper"
        };

        var options = CanonizationOptions.Default
            .WithStageBehavior(CanonStageBehavior.StageOnly)
            .WithOrigin("queue")
            .WithTag("tracking", "stage-only");
        options = options with { CorrelationId = correlation };

        var result = await runtime.Canonize(entity, options, CancellationToken.None);

        result.Outcome.Should().Be(CanonizationOutcome.Parked);
        result.DistributionSkipped.Should().BeTrue();
        result.Events.Should().ContainSingle(evt => evt.Phase == CanonPipelinePhase.Intake);
        result.Metadata.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("stage-only");

        persistence.CanonicalEntities.Should().BeEmpty();

        var stages = persistence.StageRecords.OfType<CanonStage<ContactCanon>>().ToArray();
        stages.Should().HaveCount(1);

        var stage = stages[0];
        stage.Status.Should().Be(CanonStageStatus.Pending);
        stage.CorrelationId.Should().Be(correlation);
        stage.Payload.Should().NotBeNull();
        stage.Payload!.Email.Should().Be(email);
        stage.Metadata.Should().ContainKey("runtime:stage-behavior").WhoseValue.Should().Be(CanonStageBehavior.StageOnly.ToString());
    }

    [Fact]
    public async Task Failed_phase_stops_before_aggregation_and_canonical_persistence()
    {
        var persistence = new InMemoryCanonPersistence();
        var builder = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(new NoopAuditSink());
        builder.ConfigurePipeline<ContactCanon>(pipeline =>
            pipeline.AddStep(CanonPipelinePhase.Validation, (context, cancellationToken) =>
                ValueTask.FromResult<CanonizationEvent?>(new CanonizationEvent
                {
                    Phase = CanonPipelinePhase.Validation,
                    StageStatus = CanonStageStatus.Failed,
                    CanonState = context.Entity.State,
                    Message = "Customer validation failed",
                    Detail = "Email is required"
                })));

        var result = await builder.Build().Canonize(new ContactCanon { DisplayName = "Invalid" });

        result.Outcome.Should().Be(CanonizationOutcome.Failed);
        result.DistributionSkipped.Should().BeTrue();
        result.Events.Should().ContainSingle(evt =>
            evt.Phase == CanonPipelinePhase.Validation && evt.Detail == "Email is required");
        persistence.CanonicalEntities.Should().BeEmpty();
        persistence.FindIndex(typeof(ContactCanon).FullName!, "Email=").Should().BeNull();
    }

    [Fact]
    public async Task Failed_late_phase_discards_pending_index_and_canonical_persistence()
    {
        var persistence = new InMemoryCanonPersistence();
        var builder = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(new NoopAuditSink());
        builder.ConfigurePipeline<ContactCanon>(pipeline =>
            pipeline.AddStep(CanonPipelinePhase.Policy, (context, cancellationToken) =>
                ValueTask.FromResult<CanonizationEvent?>(new CanonizationEvent
                {
                    Phase = CanonPipelinePhase.Policy,
                    StageStatus = CanonStageStatus.Failed,
                    CanonState = context.Entity.State,
                    Message = "Customer policy failed"
                })));

        const string email = "late-failure@example.com";
        var result = await builder.Build().Canonize(new ContactCanon { Email = email, DisplayName = "Invalid" });

        result.Outcome.Should().Be(CanonizationOutcome.Failed);
        result.Events.Select(static item => item.Phase).Should().Contain(CanonPipelinePhase.Aggregation);
        persistence.CanonicalEntities.Should().BeEmpty();
        persistence.FindIndex(typeof(ContactCanon).FullName!, $"Email={email}").Should().BeNull();
    }

    [Fact]
    public async Task Rebuild_views_loads_from_the_configured_persistence()
    {
        var (persistence, runtime) = BuildRuntime();
        var entity = new ContactCanon
        {
            Email = $"rebuild-{Guid.CreateVersion7():N}@example.com",
            PhoneNumber = "555-0300",
            DisplayName = "Katherine Johnson"
        };

        var canonical = await runtime.Canonize(entity, CanonizationOptions.Default.WithOrigin("unit-spec"));

        await runtime.RebuildViews<ContactCanon>(canonical.Canonical.Id, ["canonical"]);

        persistence.CanonicalReads.Should().Contain(canonical.Canonical.Id);
        persistence.CanonicalEntities.OfType<ContactCanon>()
            .Should().Contain(item => item.Id == canonical.Canonical.Id);
    }

    private static (InMemoryCanonPersistence Persistence, CanonRuntime Runtime) BuildRuntime()
    {
        var persistence = new InMemoryCanonPersistence();
        var builder = new CanonRuntimeBuilder();
        builder.UsePersistence(persistence);
        builder.UseAuditSink(new NoopAuditSink());
        builder.ConfigureDefaultOptions(options => options.WithStageBehavior(CanonStageBehavior.Immediate) with { SkipDistribution = true });
        builder.ConfigurePipeline<ContactCanon>(pipeline =>
        {
            pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
            {
                context.Entity.DisplayName = context.Entity.DisplayName?.ToUpperInvariant();
                context.Metadata.SetTag("processed", "true");
                return ValueTask.CompletedTask;
            });
        });
        return (persistence, builder.Build());
    }

    private sealed class InMemoryCanonPersistence : ICanonPersistence
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, CanonIndex> _indices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<object> _canonicalEntities = new();
        private readonly List<object> _stageRecords = new();
        private readonly List<string> _canonicalReads = new();

        public IReadOnlyCollection<object> CanonicalEntities
        {
            get
            {
                lock (_gate)
                {
                    return _canonicalEntities.ToArray();
                }
            }
        }

        public IReadOnlyCollection<object> StageRecords
        {
            get
            {
                lock (_gate)
                {
                    return _stageRecords.ToArray();
                }
            }
        }

        public IReadOnlyCollection<string> CanonicalReads
        {
            get
            {
                lock (_gate)
                {
                    return _canonicalReads.ToArray();
                }
            }
        }

        public CanonIndex? FindIndex(string entityType, string key)
        {
            lock (_gate)
            {
                return _indices.TryGetValue(MakeKey(entityType, key), out var index) ? index : null;
            }
        }

        public Task<TModel?> GetCanonicalAsync<TModel>(string canonicalId, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            lock (_gate)
            {
                _canonicalReads.Add(canonicalId);
                return Task.FromResult(_canonicalEntities
                    .OfType<TModel>()
                    .LastOrDefault(entity => string.Equals(entity.Id, canonicalId, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            lock (_gate)
            {
                _canonicalEntities.Add(entity);
            }

            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (string.IsNullOrWhiteSpace(stage.Id))
            {
                stage.Id = Guid.CreateVersion7().ToString("n");
            }

            lock (_gate)
            {
                _stageRecords.Add(stage);
            }

            return Task.FromResult(stage);
        }

        public Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _indices.TryGetValue(MakeKey(entityType, key), out var index);
                return Task.FromResult(index);
            }
        }

        public Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken)
        {
            if (index is null)
            {
                throw new ArgumentNullException(nameof(index));
            }

            lock (_gate)
            {
                _indices[MakeKey(index.EntityType, index.Key)] = index;
            }

            return Task.CompletedTask;
        }

        private static string MakeKey(string entityType, string key)
            => $"{entityType}::{key}";
    }

    private sealed class NoopAuditSink : ICanonAuditSink
    {
        public Task Write(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Canon]
    private sealed class ContactCanon : CanonEntity<ContactCanon>
    {
        [AggregationKey]
        public string Email { get; set; } = "";

        [AggregationKey]
        public string? PhoneNumber { get; set; }

        public string? DisplayName { get; set; }
    }
}
