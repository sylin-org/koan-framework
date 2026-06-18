using System.Threading;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeBuilderSpec
{
    [Fact]
    public void BuildConfiguration_captures_pipeline_metadata()
    {
        var builder = new CanonRuntimeBuilder();
        builder.ConfigureDefaultOptions(options =>
        {
            var updated = options.WithOrigin("default-origin").WithStageBehavior(CanonStageBehavior.StageOnly);
            return updated with { SkipDistribution = true };
        });
        builder.SetRecordCapacity(512);
        builder.ConfigurePipeline<TestCanon>(pipeline =>
        {
            pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
            {
                context.Metadata.SetTag("step", "intake");
                return ValueTask.CompletedTask;
            });
        });

        var configuration = builder.BuildConfiguration();

        configuration.RecordCapacity.Should().Be(512);
        configuration.DefaultOptions.Origin.Should().Be("default-origin");
        configuration.DefaultOptions.StageBehavior.Should().Be(CanonStageBehavior.StageOnly);
        configuration.DefaultOptions.SkipDistribution.Should().BeTrue();
        configuration.Pipelines.Should().ContainKey(typeof(TestCanon));
        configuration.PipelineMetadata.Should().ContainKey(typeof(TestCanon));

        var metadata = configuration.PipelineMetadata[typeof(TestCanon)];
        metadata.ModelType.Should().Be(typeof(TestCanon));
        metadata.Phases.Should().Contain(CanonPipelinePhase.Intake);
        metadata.Phases.Should().Contain(CanonPipelinePhase.Aggregation);
        metadata.HasSteps.Should().BeTrue();
        metadata.AggregationKeys.Should().Contain("Key");
        metadata.AggregationPolicies.Should().ContainKey("Key").WhoseValue.Should().Be(AggregationPolicyKind.SourceOfTruth);
        metadata.AggregationPolicyDetails.Should().ContainKey("Key");
        metadata.AggregationPolicyDetails["Key"].AuthoritativeSources.Should().Contain("crm");
    }

    [Fact]
    public void SetRecordCapacity_rejects_non_positive_values()
    {
        var builder = new CanonRuntimeBuilder();
        Action act = () => builder.SetRecordCapacity(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Build_produces_runtime_with_configured_defaults()
    {
        var persistence = new TestPersistence();

        var builder = new CanonRuntimeBuilder();
        builder.UsePersistence(persistence);
        builder.UseAuditSink(new NoopAuditSink());
        builder.ConfigureDefaultOptions(options =>
        {
            var updated = options.WithOrigin("builder-default").WithTag("tracking", "default");
            updated = updated.WithStageBehavior(CanonStageBehavior.StageOnly);
            return updated with { SkipDistribution = true };
        });
        builder.ConfigurePipeline<TestCanon>(pipeline => { });

        var runtime = builder.Build();
        var entity = new TestCanon { Key = "alpha" };

        var result = await runtime.Canonize(entity, options: null, CancellationToken.None);

        result.Outcome.Should().Be(CanonizationOutcome.Parked);
        result.Metadata.Origin.Should().Be("builder-default");
        result.Metadata.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("default");
        result.DistributionSkipped.Should().BeTrue();

        persistence.StageRecords.Should().HaveCount(1);
        var stage = persistence.StageRecords.Single();
        stage.Payload.Should().NotBeNull();
        stage.Payload!.Key.Should().Be("alpha");
        stage.Metadata.Should().ContainKey("runtime:stage-behavior").WhoseValue.Should().Be(CanonStageBehavior.StageOnly.ToString());
    }

    private sealed class TestCanon : CanonEntity<TestCanon>
    {
        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "crm")]
        public string Key { get; set; } = "";
    }

    private sealed class TestPersistence : ICanonPersistence
    {
        private readonly object _gate = new();
        private readonly List<CanonStage<TestCanon>> _stages = new();

        public IReadOnlyList<CanonStage<TestCanon>> StageRecords
        {
            get
            {
                lock (_gate)
                {
                    return _stages.ToArray();
                }
            }
        }

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(entity);

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (stage is CanonStage<TestCanon> typed)
            {
                lock (_gate)
                {
                    _stages.Add(typed);
                }
            }

            return Task.FromResult(stage);
        }

        public Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult<CanonIndex?>(null);

        public Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NoopAuditSink : ICanonAuditSink
    {
        public Task Write(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
