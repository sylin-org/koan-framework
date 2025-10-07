namespace Koan.Tests.Canon.Integration.Specs;

public sealed class CanonRuntimeFlowSpec
{
    private const string ServicesKey = "services";

    private readonly ITestOutputHelper _output;

    public CanonRuntimeFlowSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Canonization_pipeline_persists_canonical_and_indexes()
        => TestPipeline.For<CanonRuntimeFlowSpec>(_output, nameof(Canonization_pipeline_persists_canonical_and_indexes))
            .UsingServiceProvider(ServicesKey, ConfigureServices)
            .Arrange(ctx =>
            {
                using var scope = ctx.CreateServiceScope(ServicesKey);
                var runtime = scope.ServiceProvider.GetRequiredService<ICanonRuntime>();
                var persistence = scope.ServiceProvider.GetRequiredService<ICanonPersistence>() as InMemoryCanonPersistence
                    ?? throw new InvalidOperationException("In-memory persistence not registered");
                ctx.SetItem("runtime", runtime);
                ctx.SetItem("persistence", persistence);
            })
            .Act(ctx => ExecuteCanonizationAsync(ctx, CanonStageBehavior.Immediate, "integration"))
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<ContactCanon>>("result");
                var persistence = ctx.GetRequiredItem<ICanonPersistence>("persistence") as InMemoryCanonPersistence;
                persistence.Should().NotBeNull();

                result.Outcome.Should().Be(CanonizationOutcome.Canonized);
                result.Metadata.Tags.Should().ContainKey("source").WhoseValue.Should().Be("integration");
                result.Canonical.DisplayName.Should().Be("ALPHA");
                result.Canonical.Email.Should().Contain("integration");

                var stored = persistence!.GetCanonical(result.Canonical.Id);
                stored.Should().NotBeNull();
                stored!.DisplayName.Should().Be("ALPHA");

                var indexKey = $"Email={result.Canonical.Email}";
                var index = persistence.GetIndex(typeof(ContactCanon), indexKey);
                index.Should().NotBeNull();
                index!.CanonicalId.Should().Be(result.Canonical.Id);
            })
            .RunAsync();

    [Fact]
    public Task Stage_only_requests_create_stage_entries_without_persisting_canonical()
        => TestPipeline.For<CanonRuntimeFlowSpec>(_output, nameof(Stage_only_requests_create_stage_entries_without_persisting_canonical))
            .UsingServiceProvider(ServicesKey, ConfigureServices)
            .Arrange(ctx =>
            {
                using var scope = ctx.CreateServiceScope(ServicesKey);
                var runtime = scope.ServiceProvider.GetRequiredService<ICanonRuntime>();
                var persistence = scope.ServiceProvider.GetRequiredService<ICanonPersistence>() as InMemoryCanonPersistence
                    ?? throw new InvalidOperationException("In-memory persistence not registered");
                ctx.SetItem("runtime", runtime);
                ctx.SetItem("persistence", persistence);
            })
            .Act(ctx => ExecuteCanonizationAsync(ctx, CanonStageBehavior.StageOnly, "queue"))
            .Assert(ctx =>
            {
                var result = ctx.GetRequiredItem<CanonizationResult<ContactCanon>>("result");
                var persistence = ctx.GetRequiredItem<InMemoryCanonPersistence>("persistence");

                result.Outcome.Should().Be(CanonizationOutcome.Parked);
                result.Events.Should().ContainSingle(evt => evt.Phase == CanonPipelinePhase.Intake);
                persistence.StageRecords.Should().HaveCount(1);
                persistence.CanonicalEntries.Should().BeEmpty();
            })
            .RunAsync();

    private static void ConfigureServices(TestContext ctx, IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<ICanonAuditSink, NoopAuditSink>();
        services.AddSingleton<InMemoryCanonPersistence>();
        services.AddSingleton<ICanonPersistence>(sp => sp.GetRequiredService<InMemoryCanonPersistence>());
        services.AddSingleton<ICanonRuntime>(sp =>
        {
            var persistence = sp.GetRequiredService<InMemoryCanonPersistence>();
            var builder = new CanonRuntimeBuilder();
            builder.UsePersistence(persistence);
            builder.UseAuditSink(sp.GetRequiredService<ICanonAuditSink>());
            builder.ConfigurePipeline<ContactCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, cancellationToken) =>
                {
                    context.Metadata.SetTag("source", context.Options.Origin ?? "unknown");
                    context.Entity.DisplayName = context.Entity.DisplayName?.ToUpperInvariant();
                    return ValueTask.CompletedTask;
                });
            });
            return builder.Build();
        });
    }

    private static async ValueTask ExecuteCanonizationAsync(TestContext ctx, CanonStageBehavior behavior, string origin)
    {
        var runtime = ctx.GetRequiredItem<ICanonRuntime>("runtime");
        var email = $"{origin}-{ctx.ExecutionId:N}@example.com";

        var entity = new ContactCanon
        {
            Email = email,
            PhoneNumber = "555-0100",
            DisplayName = "Alpha"
        };

        var options = CanonizationOptions.Default
            .WithOrigin(origin)
            .WithStageBehavior(behavior);

        var result = await runtime.Canonize(entity, options, ctx.Cancellation).ConfigureAwait(false);
        ctx.SetItem("result", result);
    }

    private sealed class InMemoryCanonPersistence : ICanonPersistence
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, CanonIndex> _indices = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ContactCanon> _canonicals = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CanonStage<ContactCanon>> _stages = new();

        public IReadOnlyCollection<CanonStage<ContactCanon>> StageRecords
        {
            get
            {
                lock (_gate)
                {
                    return _stages.ToArray();
                }
            }
        }

        public IReadOnlyCollection<ContactCanon> CanonicalEntries
        {
            get
            {
                lock (_gate)
                {
                    return _canonicals.Values.ToArray();
                }
            }
        }

        public ContactCanon? GetCanonical(string id)
        {
            lock (_gate)
            {
                return _canonicals.TryGetValue(id, out var value) ? value : null;
            }
        }

        public CanonIndex? GetIndex(Type type, string key)
        {
            var entityType = type.FullName ?? type.Name;
            lock (_gate)
            {
                return _indices.TryGetValue(MakeKey(entityType, key), out var index) ? index : null;
            }
        }

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (entity is ContactCanon contact)
            {
                lock (_gate)
                {
                    _canonicals[entity.Id] = contact;
                }
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

            if (stage is CanonStage<ContactCanon> contactStage)
            {
                lock (_gate)
                {
                    _stages.Add(contactStage);
                }
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
