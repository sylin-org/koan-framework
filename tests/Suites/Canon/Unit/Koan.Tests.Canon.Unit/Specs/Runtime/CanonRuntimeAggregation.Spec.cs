using System.Threading;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeAggregationSpec
{
    private const string ExistingEntityContextKey = "canon:existing-entity";
    private const string ExistingMetadataContextKey = "canon:existing-metadata";

    [Fact]
    public async Task Aggregation_requires_at_least_one_key_value()
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

        var runtime = builder.Build();
        var entity = new KeylessCanon
        {
            Primary = null,
            Secondary = null
        };

        var assertion = await runtime.Awaiting(r => r.Canonize(entity, options: null, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        assertion.Which.Message.Should().Contain("requires at least one aggregation key value");
    }

    [Fact]
    public async Task Aggregation_merges_existing_identities_and_records_lineage()
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

        var runtime = builder.Build();
        var entity = new LinkedCanon
        {
            Primary = "alpha",
            Secondary = "beta"
        };

        var result = await runtime.Canonize(entity, options: null, CancellationToken.None);

        result.Canonical.Id.Should().Be("canon-alpha");
        result.Metadata.Tags.Should().ContainKey("identity:merged-from").WhoseValue.Should().Contain("canon-zeta");
        result.Metadata.Tags.Should().ContainKey("existing").WhoseValue.Should().Be("true");

        persistence.CanonicalSnapshots.Should().ContainSingle();
        persistence.Upserts.Should().HaveCount(3);
        persistence.Upserts.Should().AllSatisfy(index => index.CanonicalId.Should().Be("canon-alpha"));
        persistence.Upserts.Select(index => index.Key).Should().Contain(new[]
        {
            "Primary=alpha",
            "Secondary=beta",
            "Primary=alpha|Secondary=beta"
        });
    }

    [Fact]
    public async Task Source_of_truth_policies_preserve_existing_when_incoming_not_authoritative()
    {
        var entityType = typeof(LinkedCanon).FullName ?? typeof(LinkedCanon).Name;
        var secondaryIndex = new CanonIndex
        {
            EntityType = entityType,
            Key = "Secondary=beta",
            Kind = CanonIndexKeyKind.Aggregation
        };
        secondaryIndex.Update("canon-alpha", origin: "legacy", attributes: null);

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

        var persistence = new StubCanonPersistence((_, key) => key == secondaryIndex.Key ? secondaryIndex : null);
        persistence.SeedCanonical(existing);

        var builder = new CanonRuntimeBuilder();
        builder.UsePersistence(persistence);
        builder.ConfigurePipeline<LinkedCanon>(_ => { });

        var runtime = builder.Build();
        var entity = new LinkedCanon
        {
            Primary = "alpha-incoming",
            Secondary = "beta"
        };

        var options = CanonizationOptions.Default.WithOrigin("drip");
        var result = await runtime.Canonize(entity, options, CancellationToken.None);

        result.Canonical.Id.Should().Be("canon-alpha");
        result.Canonical.Primary.Should().Be("alpha-existing");
        result.Metadata.PropertyFootprints.Should().ContainKey(nameof(LinkedCanon.Primary));
        var footprint = result.Metadata.PropertyFootprints[nameof(LinkedCanon.Primary)];
        footprint.SourceKey.Should().Be("crm");
        footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("existing");
        persistence.CanonicalReads.Should().ContainSingle().Which.Should().Be("canon-alpha");
    }

    [Fact]
    public async Task Source_of_truth_policies_accept_authoritative_incoming_sources()
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

        var runtime = builder.Build();
        var entity = new LinkedCanon
        {
            Primary = "alpha-authoritative",
            Secondary = "beta"
        };

        var options = CanonizationOptions.Default.WithOrigin("crm");
        var result = await runtime.Canonize(entity, options, CancellationToken.None);

        result.Canonical.Id.Should().Be("canon-alpha");
        result.Canonical.Primary.Should().Be("alpha-authoritative");

        result.Metadata.PropertyFootprints.Should().ContainKey(nameof(LinkedCanon.Primary));
        var footprint = result.Metadata.PropertyFootprints[nameof(LinkedCanon.Primary)];
        footprint.SourceKey.Should().Be("crm");
        footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("incoming");

        persistence.CanonicalSnapshots.Should().ContainSingle();
        var stored = persistence.CanonicalSnapshots.Single() as LinkedCanon;
        stored.Should().NotBeNull();
        stored!.Primary.Should().Be("alpha-authoritative");
    }

    [Fact]
    public async Task Canonical_read_failures_from_custom_persistence_propagate()
    {
        var entityType = typeof(LinkedCanon).FullName ?? typeof(LinkedCanon).Name;
        var index = new CanonIndex
        {
            EntityType = entityType,
            Key = "Secondary=unavailable",
            Kind = CanonIndexKeyKind.Aggregation
        };
        index.Update("canon-unavailable", origin: "legacy", attributes: null);

        var failure = new InvalidOperationException("custom canon store unavailable");
        var persistence = new StubCanonPersistence((_, key) => key == index.Key ? index : null)
        {
            CanonicalReadFailure = failure
        };
        var builder = new CanonRuntimeBuilder();
        builder.UsePersistence(persistence);
        builder.ConfigurePipeline<LinkedCanon>(_ => { });
        var runtime = builder.Build();

        var act = () => runtime.Canonize(new LinkedCanon
        {
            Primary = "incoming",
            Secondary = "unavailable"
        });

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(failure);
    }

    private sealed class StubCanonPersistence : ICanonPersistence
    {
        private readonly Func<string, string, CanonIndex?> _lookup;
        private readonly List<CanonIndex> _upserts = new();
        private readonly List<object> _canonicals = new();
        private readonly List<object> _stages = new();
        private readonly List<string> _canonicalReads = new();

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
        public IReadOnlyList<string> CanonicalReads => _canonicalReads;
        public Exception? CanonicalReadFailure { get; init; }

        public void SeedCanonical<TModel>(TModel entity)
            where TModel : CanonEntity<TModel>, new()
            => _canonicals.Add(entity);

        public Task<TModel?> GetCanonicalAsync<TModel>(string canonicalId, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            _canonicalReads.Add(canonicalId);
            if (CanonicalReadFailure is not null)
            {
                return Task.FromException<TModel?>(CanonicalReadFailure);
            }

            return Task.FromResult(_canonicals
                .OfType<TModel>()
                .LastOrDefault(entity => string.Equals(entity.Id, canonicalId, StringComparison.OrdinalIgnoreCase)));
        }

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

        public Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken)
        {
            return Task.FromResult(_lookup(entityType, key));
        }

        public Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken)
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
        public string Primary { get; set; } = "";

        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.Latest)]
        public string? Secondary { get; set; }
    }
}
