using System.Threading;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeAggregationSpec
{
    private const string RuntimeKey = "runtime";
    private const string PersistenceKey = "persistence";
    private const string ResultKey = "result";
    private const string ExistingEntityContextKey = "canon:existing-entity";
    private const string ExistingMetadataContextKey = "canon:existing-metadata";

    private readonly ITestOutputHelper _output;

    public CanonRuntimeAggregationSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Aggregation_requires_at_least_one_key_value()
        => TestPipeline.For<CanonRuntimeAggregationSpec>(_output, nameof(Aggregation_requires_at_least_one_key_value))
            .Arrange(ctx =>
            {
                var persistence = new StubCanonPersistence();

                var builder = new CanonRuntimeBuilder();
                builder.UsePersistence(persistence);
                builder.ConfigurePipeline<KeylessCanon>(pipeline =>
                {
                    pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
                    {
                        context.Metadata.SetOrigin("unit-test");
                        return ValueTask.CompletedTask;
                    });
                });

                ctx.SetItem(PersistenceKey, persistence);
                ctx.SetItem(RuntimeKey, builder.Build());
            })
            .Act(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);
                var entity = new KeylessCanon
                {
                    Primary = null,
                    Secondary = null
                };

                var assertion = await runtime.Awaiting(r => r.Canonize(entity, options: null, ctx.Cancellation))
                    .Should().ThrowAsync<InvalidOperationException>();

                assertion.Which.Message.Should().Contain("requires at least one aggregation key value");
            })
            .RunAsync();

    [Fact]
    public Task Aggregation_merges_existing_identities_and_records_lineage()
        => TestPipeline.For<CanonRuntimeAggregationSpec>(_output, nameof(Aggregation_merges_existing_identities_and_records_lineage))
            .Arrange(ctx =>
            {
                var entityType = typeof(LinkedCanon).FullName ?? typeof(LinkedCanon).Name;
                var primaryIndex = new CanonIndex
                {
                    EntityType = entityType,
                    Key = "Primary=alpha",
                    Kind = CanonIndexKeyKind.Aggregation
                };
                primaryIndex.Update("canon-alpha", origin: "legacy", attributes: null);

                var secondaryIndex = new CanonIndex
                {
                    EntityType = entityType,
                    Key = "Secondary=beta",
                    Kind = CanonIndexKeyKind.Aggregation
                };
                secondaryIndex.Update("canon-zeta", origin: "legacy", attributes: null);

                var lookup = new Dictionary<string, CanonIndex?>
                {
                    [primaryIndex.Key] = primaryIndex,
                    [secondaryIndex.Key] = secondaryIndex
                };

                var persistence = new StubCanonPersistence((entityType, key) => lookup.TryGetValue(key, out var index) ? index : null);

                var builder = new CanonRuntimeBuilder();
                builder.UsePersistence(persistence);
                builder.ConfigurePipeline<LinkedCanon>(pipeline =>
                {
                    pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
                    {
                        var existing = new LinkedCanon
                        {
                            Primary = context.Entity.Primary,
                            Secondary = context.Entity.Secondary
                        };

                        existing.Id = "canon-alpha";
                        existing.Metadata.AssignCanonicalId("canon-alpha");
                        existing.Metadata.SetTag("existing", "true");

                        context.SetItem(ExistingEntityContextKey, existing);
                        context.SetItem(ExistingMetadataContextKey, existing.Metadata.Clone());

                        return ValueTask.CompletedTask;
                    });

                    pipeline.AddStep(CanonPipelinePhase.Policy, (context, cancellationToken) =>
                    {
                        context.Metadata.RecordPolicy(new CanonPolicySnapshot
                        {
                            Policy = "Primary:SourceOfTruth",
                            Outcome = "existing",
                            Evidence = new Dictionary<string, string?> { ["winner"] = "existing" }
                        });

                        return ValueTask.CompletedTask;
                    });
                });

                ctx.SetItem(PersistenceKey, persistence);
                ctx.SetItem(RuntimeKey, builder.Build());
            })
            .Act(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);
                var entity = new LinkedCanon
                {
                    Primary = "alpha",
                    Secondary = "beta"
                };

                var result = await runtime.Canonize(entity, options: null, ctx.Cancellation).ConfigureAwait(false);
                ctx.SetItem(ResultKey, result);
            })
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<LinkedCanon>>(ResultKey);
                result.Canonical.Id.Should().Be("canon-alpha");
                result.Metadata.Tags.Should().ContainKey("identity:merged-from").WhoseValue.Should().Contain("canon-zeta");
                result.Metadata.Tags.Should().ContainKey("existing").WhoseValue.Should().Be("true");

                var persistence = ctx.GetRequiredItem<StubCanonPersistence>(PersistenceKey);
                persistence.CanonicalSnapshots.Should().ContainSingle();
                persistence.Upserts.Should().HaveCount(3);
                persistence.Upserts.Should().AllSatisfy(index => index.CanonicalId.Should().Be("canon-alpha"));
                persistence.Upserts.Select(index => index.Key).Should().Contain(new[]
                {
                    "Primary=alpha",
                    "Secondary=beta",
                    "Primary=alpha|Secondary=beta"
                });

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Source_of_truth_policies_preserve_existing_when_incoming_not_authoritative()
        => TestPipeline.For<CanonRuntimeAggregationSpec>(_output, nameof(Source_of_truth_policies_preserve_existing_when_incoming_not_authoritative))
            .Arrange(ctx =>
            {
                var entityType = typeof(LinkedCanon).FullName ?? typeof(LinkedCanon).Name;
                var secondaryIndex = new CanonIndex
                {
                    EntityType = entityType,
                    Key = "Secondary=beta",
                    Kind = CanonIndexKeyKind.Aggregation
                };
                secondaryIndex.Update("canon-alpha", origin: "legacy", attributes: null);

                var persistence = new StubCanonPersistence((_, key) => key == secondaryIndex.Key ? secondaryIndex : null);

                var builder = new CanonRuntimeBuilder();
                builder.UsePersistence(persistence);
                builder.ConfigurePipeline<LinkedCanon>(pipeline =>
                {
                    pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
                    {
                        var existing = new LinkedCanon
                        {
                            Primary = "alpha-existing",
                            Secondary = "beta"
                        };
                        existing.Id = "canon-alpha";
                        existing.Metadata.AssignCanonicalId("canon-alpha");
                        existing.Metadata.SetOrigin("crm");
                        existing.Metadata.PropertyFootprints[nameof(LinkedCanon.Primary)] = new CanonPropertyFootprint
                        {
                            Property = nameof(LinkedCanon.Primary),
                            SourceKey = "crm",
                            ArrivalToken = "existing-token",
                            ArrivedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                            Value = "alpha-existing",
                            Policy = "Primary:SourceOfTruth",
                            Evidence = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["winner"] = "existing"
                            }
                        };

                        context.SetItem(ExistingEntityContextKey, existing);
                        context.SetItem(ExistingMetadataContextKey, existing.Metadata.Clone());
                        return ValueTask.CompletedTask;
                    });
                });

                ctx.SetItem(PersistenceKey, persistence);
                ctx.SetItem(RuntimeKey, builder.Build());
            })
            .Act(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);
                var entity = new LinkedCanon
                {
                    Primary = "alpha-incoming",
                    Secondary = "beta"
                };

                var options = CanonizationOptions.Default.WithOrigin("drip");
                var result = await runtime.Canonize(entity, options, ctx.Cancellation).ConfigureAwait(false);
                ctx.SetItem(ResultKey, result);
            })
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<LinkedCanon>>(ResultKey);
                result.Canonical.Id.Should().Be("canon-alpha");
                result.Canonical.Primary.Should().Be("alpha-existing");
                result.Metadata.PropertyFootprints.Should().ContainKey(nameof(LinkedCanon.Primary));
                var footprint = result.Metadata.PropertyFootprints[nameof(LinkedCanon.Primary)];
                footprint.SourceKey.Should().Be("crm");
                footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("existing");

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Source_of_truth_policies_accept_authoritative_incoming_sources()
        => TestPipeline.For<CanonRuntimeAggregationSpec>(_output, nameof(Source_of_truth_policies_accept_authoritative_incoming_sources))
            .Arrange(ctx =>
            {
                var entityType = typeof(LinkedCanon).FullName ?? typeof(LinkedCanon).Name;
                var secondaryIndex = new CanonIndex
                {
                    EntityType = entityType,
                    Key = "Secondary=beta",
                    Kind = CanonIndexKeyKind.Aggregation
                };
                secondaryIndex.Update("canon-alpha", origin: "legacy", attributes: null);

                var persistence = new StubCanonPersistence((_, key) => key == secondaryIndex.Key ? secondaryIndex : null);

                var builder = new CanonRuntimeBuilder();
                builder.UsePersistence(persistence);
                builder.ConfigurePipeline<LinkedCanon>(pipeline =>
                {
                    pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
                    {
                        var existing = new LinkedCanon
                        {
                            Primary = "alpha-existing",
                            Secondary = "beta"
                        };
                        existing.Id = "canon-alpha";
                        existing.Metadata.AssignCanonicalId("canon-alpha");
                        existing.Metadata.PropertyFootprints[nameof(LinkedCanon.Primary)] = new CanonPropertyFootprint
                        {
                            Property = nameof(LinkedCanon.Primary),
                            SourceKey = "crm",
                            ArrivalToken = "existing-token",
                            ArrivedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                            Value = "alpha-existing",
                            Policy = "Primary:SourceOfTruth",
                            Evidence = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["winner"] = "existing"
                            }
                        };

                        context.SetItem(ExistingEntityContextKey, existing);
                        context.SetItem(ExistingMetadataContextKey, existing.Metadata.Clone());
                        return ValueTask.CompletedTask;
                    });
                });

                ctx.SetItem(PersistenceKey, persistence);
                ctx.SetItem(RuntimeKey, builder.Build());
            })
            .Act(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);
                var entity = new LinkedCanon
                {
                    Primary = "alpha-authoritative",
                    Secondary = "beta"
                };

                var options = CanonizationOptions.Default.WithOrigin("crm");
                var result = await runtime.Canonize(entity, options, ctx.Cancellation).ConfigureAwait(false);
                ctx.SetItem(ResultKey, result);
            })
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<LinkedCanon>>(ResultKey);
                result.Canonical.Id.Should().Be("canon-alpha");
                result.Canonical.Primary.Should().Be("alpha-authoritative");

                result.Metadata.PropertyFootprints.Should().ContainKey(nameof(LinkedCanon.Primary));
                var footprint = result.Metadata.PropertyFootprints[nameof(LinkedCanon.Primary)];
                footprint.SourceKey.Should().Be("crm");
                footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("incoming");

                var persistence = ctx.GetRequiredItem<StubCanonPersistence>(PersistenceKey);
                persistence.CanonicalSnapshots.Should().ContainSingle();
                var stored = persistence.CanonicalSnapshots.Single() as LinkedCanon;
                stored.Should().NotBeNull();
                stored!.Primary.Should().Be("alpha-authoritative");

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    private sealed class StubCanonPersistence : ICanonPersistence
    {
        private readonly Func<string, string, CanonIndex?> _lookup;
        private readonly List<CanonIndex> _upserts = new();
        private readonly List<object> _canonicals = new();
        private readonly List<object> _stages = new();

        public StubCanonPersistence()
            : this((_, _) => null)
        {
        }

        public StubCanonPersistence(Func<string, string, CanonIndex?> lookup)
        {
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        }

        public IReadOnlyList<CanonIndex> Upserts => _upserts;
        public IReadOnlyList<object> CanonicalSnapshots => _canonicals;
        public IReadOnlyList<object> StageSnapshots => _stages;

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken) where TModel : CanonEntity<TModel>, new()
        {
            _canonicals.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken) where TModel : CanonEntity<TModel>, new()
        {
            _stages.Add(stage);
            return Task.FromResult(stage);
        }

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
        {
            return Task.FromResult(_lookup(entityType, key));
        }

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
        {
            _upserts.Add(index);
            return Task.CompletedTask;
        }
    }

    private sealed class KeylessCanon : CanonEntity<KeylessCanon>
    {
        [AggregationKey]
        public string? Primary { get; set; }

        [AggregationKey]
        public string? Secondary { get; set; }
    }

    private sealed class LinkedCanon : CanonEntity<LinkedCanon>
    {
        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "crm", Sources = new[] { "erp" }, Fallback = AggregationPolicyKind.Latest)]
        public string Primary { get; set; } = string.Empty;

        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.Latest)]
        public string? Secondary { get; set; }
    }
}