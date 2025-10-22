namespace Koan.Tests.Canon.Integration.Specs;

public sealed class CanonParkAndSweepFlowSpec
{
    private const string ServicesKey = "services";
    private const string RuntimeKey = "runtime";
    private const string HarnessKey = "harness";
    private const string ResultKey = "result";
    private const string ParkedSnapshotKey = "parked:before-purge";
    private static readonly TimeSpan ParkedTtl = TimeSpan.FromSeconds(2);

    private readonly ITestOutputHelper _output;

    public CanonParkAndSweepFlowSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Park_and_sweep_parks_keyless_stage_and_purges_after_ttl()
        => TestPipeline.For<CanonParkAndSweepFlowSpec>(_output, nameof(Park_and_sweep_parks_keyless_stage_and_purges_after_ttl))
            .UsingServiceProvider(ServicesKey, ConfigureServices)
            .Arrange(ctx =>
            {
                using var scope = ctx.CreateServiceScope(ServicesKey);
                ctx.SetItem(RuntimeKey, scope.ServiceProvider.GetRequiredService<ICanonRuntime>());
                ctx.SetItem(HarnessKey, scope.ServiceProvider.GetRequiredService<ParkAndSweepHarness>());
            })
            .Act(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);

                var inbound = new KeylessCanon
                {
                    Dummy = null,
                    DisplayName = "Keyless"
                };

                var options = CanonizationOptions.Default
                    .WithStageBehavior(CanonStageBehavior.StageOnly)
                    .WithOrigin("ingest")
                    .WithTag("tenant", "acme");

                var result = await runtime.Canonize(inbound, options, ctx.Cancellation).ConfigureAwait(false);
                ctx.SetItem(ResultKey, result);

                var harness = ctx.GetRequiredItem<ParkAndSweepHarness>(HarnessKey);
                await RunParkAndSweepAsync(harness, DateTimeOffset.UtcNow).ConfigureAwait(false);

                ctx.SetItem(ParkedSnapshotKey, harness.Parked.ToArray());

                harness.PurgeExpiredParked(ParkedTtl, DateTimeOffset.UtcNow.AddSeconds(3));
            })
            .Assert(ctx =>
            {
                var harness = ctx.GetRequiredItem<ParkAndSweepHarness>(HarnessKey);
                var result = ctx.GetRequiredItem<CanonizationResult<KeylessCanon>>(ResultKey);
                var parkedBefore = ctx.GetRequiredItem<ParkAndSweepHarness.ParkedRecord[]>(ParkedSnapshotKey);

                result.Outcome.Should().Be(CanonizationOutcome.Parked);
                result.Events.Should().ContainSingle(evt => evt.Phase == CanonPipelinePhase.Intake);

                var stageEvent = result.Events.Single();
                var stageId = stageEvent.Detail?.Replace("stage:", string.Empty);

                harness.StageRecords.Should().BeEmpty();

                harness.Rejections.Should().ContainSingle(rejection => rejection.ReasonCode == ParkAndSweepHarness.NoKeysReasonCode);

                parkedBefore.Should().ContainSingle(record => record.ReasonCode == ParkAndSweepHarness.NoKeysReasonCode && record.StageId == stageId);

                harness.Parked.Should().BeEmpty();

                harness.Rejections.Should().ContainSingle();

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    private static void ConfigureServices(TestContext ctx, IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<ParkAndSweepHarness>();
        services.AddSingleton<ICanonPersistence>(sp => sp.GetRequiredService<ParkAndSweepHarness>());
        services.AddSingleton<ICanonAuditSink, NoopAuditSink>();
        services.AddSingleton<ICanonRuntime>(sp =>
        {
            var harness = sp.GetRequiredService<ParkAndSweepHarness>();
            var builder = new CanonRuntimeBuilder();
            builder.UsePersistence(harness);
            builder.UseAuditSink(sp.GetRequiredService<ICanonAuditSink>());
            builder.ConfigurePipeline<KeylessCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
                {
                    context.Metadata.SetOrigin(context.Options.Origin ?? "ingest");
                    context.Metadata.SetTag("tenant", context.Options.Tags.TryGetValue("tenant", out var value) ? value ?? string.Empty : string.Empty);
                    return ValueTask.CompletedTask;
                });
            });

            return builder.Build();
        });
    }

    private static Task RunParkAndSweepAsync(ParkAndSweepHarness harness, DateTimeOffset referenceTime)
    {
        foreach (var stage in harness.StageRecords.ToArray())
        {
            if (string.IsNullOrWhiteSpace(stage.Payload?.Dummy))
            {
                harness.ParkStage(stage, ParkAndSweepHarness.NoKeysReasonCode, referenceTime);
            }
        }

        return Task.CompletedTask;
    }

    private sealed class ParkAndSweepHarness : ICanonPersistence
    {
        public const string NoKeysReasonCode = "canon:rejection:no-keys";

        private readonly object _gate = new();
        private readonly List<CanonStage<KeylessCanon>> _stages = new();
        private readonly List<ParkedRecord> _parked = new();
        private readonly List<RejectionEntry> _rejections = new();

        public IReadOnlyCollection<CanonStage<KeylessCanon>> StageRecords
        {
            get
            {
                lock (_gate)
                {
                    return _stages.ToArray();
                }
            }
        }

        public IReadOnlyCollection<ParkedRecord> Parked
        {
            get
            {
                lock (_gate)
                {
                    return _parked.ToArray();
                }
            }
        }

        public IReadOnlyCollection<RejectionEntry> Rejections
        {
            get
            {
                lock (_gate)
                {
                    return _rejections.ToArray();
                }
            }
        }

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(entity);

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (stage is CanonStage<KeylessCanon> typed)
            {
                lock (_gate)
                {
                    if (string.IsNullOrWhiteSpace(stage.Id))
                    {
                        stage.Id = Guid.CreateVersion7().ToString("n");
                    }

                    _stages.Add(typed);
                }
            }

            return Task.FromResult(stage);
        }

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult<CanonIndex?>(null);

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public void ParkStage(CanonStage<KeylessCanon> stage, string reasonCode, DateTimeOffset parkedAt)
        {
            lock (_gate)
            {
                _stages.Remove(stage);

                var rejection = new RejectionEntry
                {
                    StageId = stage.Id,
                    ReasonCode = reasonCode,
                    RecordedAt = DateTimeOffset.UtcNow
                };

                _rejections.Add(rejection);

                _parked.Add(new ParkedRecord
                {
                    StageId = stage.Id,
                    ReasonCode = reasonCode,
                    ParkedAt = parkedAt
                });
            }
        }

        public void PurgeExpiredParked(TimeSpan ttl, DateTimeOffset now)
        {
            lock (_gate)
            {
                _parked.RemoveAll(record => now - record.ParkedAt >= ttl);
            }
        }

        internal sealed class ParkedRecord
        {
            public required string StageId { get; init; }
            public required string ReasonCode { get; init; }
            public required DateTimeOffset ParkedAt { get; init; }
        }

        internal sealed class RejectionEntry
        {
            public required string StageId { get; init; }
            public required string ReasonCode { get; init; }
            public required DateTimeOffset RecordedAt { get; init; }
        }
    }

    private sealed class NoopAuditSink : ICanonAuditSink
    {
        public Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Canon]
    private sealed class KeylessCanon : CanonEntity<KeylessCanon>
    {
        [AggregationKey]
        public string? Dummy { get; set; }

        public string? DisplayName { get; set; }
    }
}