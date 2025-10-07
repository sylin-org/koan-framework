namespace Koan.Tests.Canon.Integration.Specs;

public sealed class CanonProjectionFlowSpec
{
    private const string ServicesKey = "services";
    private const string RuntimeKey = "runtime";
    private const string HarnessKey = "harness";
    private const string InitialResultKey = "result:initial";
    private const string UpdatedResultKey = "result:updated";

    private readonly ITestOutputHelper _output;

    public CanonProjectionFlowSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Projection_pipeline_materializes_views_and_policy_state()
        => TestPipeline.For<CanonProjectionFlowSpec>(_output, nameof(Projection_pipeline_materializes_views_and_policy_state))
            .UsingServiceProvider(ServicesKey, ConfigureServices)
            .Arrange(ctx =>
            {
                using var scope = ctx.CreateServiceScope(ServicesKey);
                ctx.SetItem(RuntimeKey, scope.ServiceProvider.GetRequiredService<ICanonRuntime>());
                ctx.SetItem(HarnessKey, scope.ServiceProvider.GetRequiredService<ProjectionHarness>());
            })
            .Act(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<ICanonRuntime>(RuntimeKey);

                var options = CanonizationOptions.Default
                    .WithOrigin("crm")
                    .WithStageBehavior(CanonStageBehavior.Immediate)
                    .WithTag("tracking", "enabled");
                var initial = new CustomerCanon
                {
                    Email = "ann@example.com",
                    Dummy = "alpha",
                    DisplayName = "Ann Lee"
                };
                var initialResult = await runtime.Canonize(initial, options, ctx.Cancellation).ConfigureAwait(false);
                ctx.SetItem(InitialResultKey, initialResult);

                var updated = new CustomerCanon
                {
                    Email = "ann@example.com",
                    Dummy = "alpha",
                    DisplayName = "Annabelle Lee"
                };
                var updatedResult = await runtime.Canonize(updated, options, ctx.Cancellation).ConfigureAwait(false);
                ctx.SetItem(UpdatedResultKey, updatedResult);
            })
            .Assert(ctx =>
            {
                var harness = ctx.GetRequiredItem<ProjectionHarness>(HarnessKey);
                var initial = ctx.GetRequiredItem<CanonizationResult<CustomerCanon>>(InitialResultKey);
                var updated = ctx.GetRequiredItem<CanonizationResult<CustomerCanon>>(UpdatedResultKey);

                initial.Canonical.Id.Should().NotBeNullOrWhiteSpace();
                updated.Canonical.Id.Should().Be(initial.Canonical.Id);

                harness.Canonical.Should().ContainSingle();
                var canonical = harness.Canonical.Single();

                canonical.Id.Should().Be(updated.Canonical.Id);
                canonical.DisplayName.Should().Be("ANNABELLE LEE");
                canonical.Metadata.Origin.Should().Be("crm");
                canonical.Metadata.Tags.Should().ContainKey("tracking").WhoseValue.Should().Be("enabled");
                canonical.Metadata.Policies.Should().ContainKey("Name.First:SourceOfTruth").WhoseValue.Outcome.Should().Be("incoming");
                canonical.Metadata.PropertyFootprints.Should().ContainKey("Name.First");
                harness.Indices.Should().Contain(index => index.Key == "Email=ann@example.com");
                harness.Indices.Should().Contain(index => index.Key == "Dummy=alpha");
                harness.Indices.Should().Contain(index => index.Key.Contains("Email=ann@example.com|Dummy=alpha", StringComparison.OrdinalIgnoreCase));
                harness.Views.Should().ContainKey(ProjectionHarness.CanonicalViewKey(canonical.Id));
                var canonicalView = harness.Views[ProjectionHarness.CanonicalViewKey(canonical.Id)];
                canonicalView.ViewName.Should().Be("canonical");
                canonicalView.DisplayName.Should().Be("ANNABELLE LEE");
                canonicalView.Metadata.Tags.Should().ContainKey("tracking");

                harness.Lineage.Should().ContainSingle();
                var lineageView = harness.Lineage.Single();
                lineageView.ReferenceId.Should().Be(canonical.Id);
                lineageView.ViewName.Should().Be("lineage");
                lineageView.Sources.Should().ContainKey("crm").WhoseValue.Should().Be("CRM");

                harness.PolicyStates.Should().ContainSingle();
                var policy = harness.PolicyStates.Single();
                policy.ReferenceId.Should().Be(canonical.Id);
                policy.Policies.Should().ContainKey("Name.First:SourceOfTruth").WhoseValue.Should().Be("incoming");
                policy.PropertyFootprints.Should().ContainKey("Name.First");

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    private static void ConfigureServices(TestContext ctx, IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<ProjectionHarness>();
        services.AddSingleton<ICanonPersistence>(sp => sp.GetRequiredService<ProjectionHarness>());
        services.AddSingleton<ICanonAuditSink, NoopAuditSink>();
        services.AddSingleton<ICanonRuntime>(sp =>
        {
            var harness = sp.GetRequiredService<ProjectionHarness>();
            var audit = sp.GetRequiredService<ICanonAuditSink>();
            var builder = new CanonRuntimeBuilder();
            builder.UsePersistence(harness);
            builder.UseAuditSink(audit);
            builder.SetRecordCapacity(64);
            builder.ConfigureDefaultOptions(options => options with { SkipDistribution = true });
            builder.ConfigurePipeline<CustomerCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
                {
                    context.Metadata.RecordSource("crm", attribution =>
                    {
                        attribution.DisplayName = "CRM";
                        attribution.SetAttribute("region", "east");
                    });

                    if (context.Options.Tags.TryGetValue("tracking", out var trackingValue) && trackingValue is { Length: > 0 })
                    {
                        context.Metadata.SetTag("tracking", trackingValue);
                    }
                    else
                    {
                        context.Metadata.RemoveTag("tracking");
                    }
                    context.Entity.DisplayName = context.Entity.DisplayName?.ToUpperInvariant();
                    context.Metadata.AssignCanonicalId(context.Entity.Id);
                    context.Metadata.PropertyFootprints["Email"] = new CanonPropertyFootprint
                    {
                        Property = "Email",
                        SourceKey = "crm",
                        Value = context.Entity.Email,
                        ArrivalToken = context.Entity.Id,
                        ArrivedAt = DateTimeOffset.UtcNow,
                        Policy = "Email:Latest"
                    };

                    return ValueTask.CompletedTask;
                });

                pipeline.AddStep(CanonPipelinePhase.Policy, (context, cancellationToken) =>
                {
                    var snapshot = new CanonPolicySnapshot
                    {
                        Policy = "Name.First:SourceOfTruth",
                        Outcome = "incoming",
                        AppliedAt = DateTimeOffset.UtcNow,
                        Evidence = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["winner"] = "incoming"
                        }
                    };

                    context.Metadata.RecordPolicy(snapshot);
                    context.Metadata.PropertyFootprints["Name.First"] = new CanonPropertyFootprint
                    {
                        Property = "Name.First",
                        SourceKey = "crm",
                        Value = context.Entity.DisplayName,
                        ArrivalToken = context.Entity.Id,
                        ArrivedAt = DateTimeOffset.UtcNow,
                        Policy = snapshot.Policy,
                        Evidence = snapshot.Evidence
                    };

                    return ValueTask.CompletedTask;
                });
            });

            return builder.Build();
        });
    }

    private sealed class ProjectionHarness : ICanonPersistence
    {
        private readonly object _gate = new();
        private readonly List<CustomerSnapshot> _canonical = new();
        private readonly List<CanonStage<CustomerCanon>> _stages = new();
        private readonly Dictionary<(string EntityType, string Key), CanonIndex> _indices = new();
        private readonly Dictionary<string, CanonicalProjection> _views = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<LineageProjection> _lineage = new();
        private readonly List<PolicyProjection> _policies = new();

        public IReadOnlyCollection<CustomerSnapshot> Canonical
        {
            get
            {
                lock (_gate)
                {
                    return _canonical.ToArray();
                }
            }
        }

        public IReadOnlyCollection<CanonStage<CustomerCanon>> StageRecords
        {
            get
            {
                lock (_gate)
                {
                    return _stages.ToArray();
                }
            }
        }

        public IReadOnlyCollection<CanonIndex> Indices
        {
            get
            {
                lock (_gate)
                {
                    return _indices.Values.ToArray();
                }
            }
        }

        public IReadOnlyDictionary<string, CanonicalProjection> Views
        {
            get
            {
                lock (_gate)
                {
                    return new Dictionary<string, CanonicalProjection>(_views, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public IReadOnlyCollection<LineageProjection> Lineage
        {
            get
            {
                lock (_gate)
                {
                    return _lineage.ToArray();
                }
            }
        }

        public IReadOnlyCollection<PolicyProjection> PolicyStates
        {
            get
            {
                lock (_gate)
                {
                    return _policies.ToArray();
                }
            }
        }

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (entity is CustomerCanon customer)
            {
                lock (_gate)
                {
                    var snapshot = CustomerSnapshot.From(customer);
                    _canonical.RemoveAll(existing => existing.Id == snapshot.Id);
                    _canonical.Add(snapshot);
                    UpdateProjections(snapshot);
                }
            }

            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (stage is CanonStage<CustomerCanon> customerStage)
            {
                lock (_gate)
                {
                    _stages.Add(customerStage);
                }
            }

            return Task.FromResult(stage);
        }

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _indices.TryGetValue((entityType, key), out var index);
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
                _indices[(index.EntityType, index.Key)] = index;
                if (!string.IsNullOrWhiteSpace(index.CanonicalId))
                {
                    var snapshot = _canonical.FirstOrDefault(entry => entry.Id == index.CanonicalId);
                    if (snapshot is not null)
                    {
                        UpdateProjections(snapshot);
                    }
                }
            }

            return Task.CompletedTask;
        }

        private void UpdateProjections(CustomerSnapshot snapshot)
        {
            var canonicalView = new CanonicalProjection
            {
                ReferenceId = snapshot.Id,
                ViewName = "canonical",
                DisplayName = snapshot.DisplayName,
                Metadata = snapshot.Metadata.Clone()
            };
            _views[CanonicalViewKey(snapshot.Id)] = canonicalView;

            var sources = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in snapshot.Metadata.Sources)
            {
                var attribution = pair.Value;
                sources[pair.Key] = attribution.DisplayName ?? attribution.Key;
            }

            var lineage = new LineageProjection
            {
                ReferenceId = snapshot.Id,
                ViewName = "lineage",
                Sources = sources
            };
            _lineage.RemoveAll(existing => existing.ReferenceId == snapshot.Id);
            _lineage.Add(lineage);

            var policies = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in snapshot.Metadata.Policies)
            {
                policies[pair.Key] = pair.Value.Outcome;
            }

            var policy = new PolicyProjection
            {
                ReferenceId = snapshot.Id,
                Policies = policies,
                PropertyFootprints = snapshot.Metadata.PropertyFootprints.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase)
            };
            _policies.RemoveAll(existing => existing.ReferenceId == snapshot.Id);
            _policies.Add(policy);
        }

        public static string CanonicalViewKey(string referenceId) => $"canonical::{referenceId}";
        public static string LineageViewKey(string referenceId) => $"lineage::{referenceId}";

        internal sealed class CustomerSnapshot
        {
            public required string Id { get; init; }
            public required string DisplayName { get; init; }
            public required CanonMetadata Metadata { get; init; }

            public static CustomerSnapshot From(CustomerCanon customer)
                => new()
                {
                    Id = customer.Id,
                    DisplayName = customer.DisplayName ?? string.Empty,
                    Metadata = customer.Metadata.Clone()
                };
        }

        internal sealed class CanonicalProjection
        {
            public string ReferenceId { get; init; } = string.Empty;
            public string ViewName { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public CanonMetadata Metadata { get; init; } = new();
        }

        internal sealed class LineageProjection
        {
            public string ReferenceId { get; init; } = string.Empty;
            public string ViewName { get; init; } = string.Empty;
            public Dictionary<string, string?> Sources { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class PolicyProjection
        {
            public string ReferenceId { get; init; } = string.Empty;
            public Dictionary<string, string?> Policies { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, CanonPropertyFootprint> PropertyFootprints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class NoopAuditSink : ICanonAuditSink
    {
        public Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Canon]
    private sealed class CustomerCanon : CanonEntity<CustomerCanon>
    {
        [AggregationKey]
        public string Email { get; set; } = string.Empty;

        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "crm", Sources = new[] { "erp" })]
        public string Dummy { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
    }
}