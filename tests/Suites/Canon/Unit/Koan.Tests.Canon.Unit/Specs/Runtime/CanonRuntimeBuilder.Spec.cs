namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeBuilderSpec
{
    private readonly ITestOutputHelper _output;

    public CanonRuntimeBuilderSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task BuildConfiguration_captures_pipeline_metadata()
        => TestPipeline.For<CanonRuntimeBuilderSpec>(_output, nameof(BuildConfiguration_captures_pipeline_metadata))
            .Act(ctx =>
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

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task SetRecordCapacity_rejects_non_positive_values()
        => TestPipeline.For<CanonRuntimeBuilderSpec>(_output, nameof(SetRecordCapacity_rejects_non_positive_values))
            .Act(ctx =>
            {
                var builder = new CanonRuntimeBuilder();
                Action act = () => builder.SetRecordCapacity(0);
                act.Should().Throw<ArgumentOutOfRangeException>();
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Build_produces_runtime_with_configured_defaults()
        => TestPipeline.For<CanonRuntimeBuilderSpec>(_output, nameof(Build_produces_runtime_with_configured_defaults))
            .Arrange(ctx =>
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

                ctx.SetItem("persistence", persistence);
                ctx.SetItem("runtime", builder.Build());
            })
            .Act(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<ICanonRuntime>("runtime");
                var entity = new TestCanon { Key = "alpha" };

                var result = await runtime.Canonize(entity, options: null, ctx.Cancellation).ConfigureAwait(false);
                ctx.SetItem("result", result);
            })
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<TestCanon>>("result");
                result.Outcome.Should().Be(CanonizationOutcome.Parked);
                result.Metadata.Origin.Should().Be("builder-default");
                result.Metadata.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("default");
                result.DistributionSkipped.Should().BeTrue();

                var persistence = ctx.GetRequiredItem<TestPersistence>("persistence");
                persistence.StageRecords.Should().HaveCount(1);
                var stage = persistence.StageRecords.Single();
                stage.Payload.Should().NotBeNull();
                stage.Payload!.Key.Should().Be("alpha");
                stage.Metadata.Should().ContainKey("runtime:stage-behavior").WhoseValue.Should().Be(CanonStageBehavior.StageOnly.ToString());
            })
            .RunAsync();

    private sealed class TestCanon : CanonEntity<TestCanon>
    {
        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "crm")]
        public string Key { get; set; } = string.Empty;
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

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult<CanonIndex?>(null);

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NoopAuditSink : ICanonAuditSink
    {
        public Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
