namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeSpec
{
    private const string PersistenceKey = "canon:persistence";
    private const string RuntimeKey = "canon:runtime";
    private const string ResultKey = "canon:result";
    private const string StageResultKey = "canon:stage-result";
    private const string StageCorrelationKey = "canon:stage-correlation";
    private const string EmailKey = "canon:email";

    private readonly ITestOutputHelper _output;

    public CanonRuntimeSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Canonize_persists_entity_and_records_index()
        => TestPipeline.For<CanonRuntimeSpec>(_output, nameof(Canonize_persists_entity_and_records_index))
            .Arrange(ctx =>
            {
                var (persistence, runtime) = BuildRuntime();
                ctx.SetItem(PersistenceKey, persistence);
                ctx.SetItem(RuntimeKey, runtime);
            })
            .Act(ctx => new ValueTask(ExecuteCanonizeAsync(ctx)))
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<ContactCanon>>(ResultKey);
                var persistence = ctx.GetRequiredItem<InMemoryCanonPersistence>(PersistenceKey);
                var email = ctx.GetRequiredItem<string>(EmailKey);
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
            })
            .RunAsync();

    [Fact]
    public Task Stage_only_requests_create_stage_record()
        => TestPipeline.For<CanonRuntimeSpec>(_output, nameof(Stage_only_requests_create_stage_record))
            .Arrange(ctx =>
            {
                var (persistence, runtime) = BuildRuntime();
                ctx.SetItem(PersistenceKey, persistence);
                ctx.SetItem(RuntimeKey, runtime);
            })
            .Act(ctx => new ValueTask(ExecuteStageOnlyAsync(ctx)))
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<ContactCanon>>(StageResultKey);
                var persistence = ctx.GetRequiredItem<InMemoryCanonPersistence>(PersistenceKey);
                var email = ctx.GetRequiredItem<string>(EmailKey);
                var correlation = ctx.GetRequiredItem<string>(StageCorrelationKey);

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
            })
            .RunAsync();

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

    private async Task ExecuteCanonizeAsync(TestContext ctx)
    {
        var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);
        var email = $"canon-{ctx.ExecutionId:N}@example.com";
        ctx.SetItem(EmailKey, email);

        var entity = new ContactCanon
        {
            Email = email,
            PhoneNumber = "555-0100",
            DisplayName = "Ada Lovelace"
        };

        var options = CanonizationOptions.Default
            .WithOrigin("unit-spec")
            .WithTag("tracking", "enabled");

        var result = await runtime.Canonize(entity, options, ctx.Cancellation).ConfigureAwait(false);
        ctx.SetItem(ResultKey, result);
    }

    private async Task ExecuteStageOnlyAsync(TestContext ctx)
    {
        var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);
        var email = $"stage-{ctx.ExecutionId:N}@example.com";
        ctx.SetItem(EmailKey, email);
        var correlation = $"corr-{ctx.ExecutionId:N}";

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

        var result = await runtime.Canonize(entity, options, ctx.Cancellation).ConfigureAwait(false);
        ctx.SetItem(StageResultKey, result);
        ctx.SetItem(StageCorrelationKey, correlation);
    }

    private sealed class InMemoryCanonPersistence : ICanonPersistence
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, CanonIndex> _indices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<object> _canonicalEntities = new();
        private readonly List<object> _stageRecords = new();

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

        public CanonIndex? FindIndex(string entityType, string key)
        {
            lock (_gate)
            {
                return _indices.TryGetValue(MakeKey(entityType, key), out var index) ? index : null;
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

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _indices.TryGetValue(MakeKey(entityType, key), out var index);
                return Task.FromResult(index);
            }
        }

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
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
        public Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Canon]
    private sealed class ContactCanon : CanonEntity<ContactCanon>
    {
        [AggregationKey]
        public string Email { get; set; } = string.Empty;

        [AggregationKey]
        public string? PhoneNumber { get; set; }

        public string? DisplayName { get; set; }
    }
}
