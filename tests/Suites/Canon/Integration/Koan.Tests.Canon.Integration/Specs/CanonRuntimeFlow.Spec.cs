namespace Koan.Tests.Canon.Integration.Specs;

public sealed class CanonRuntimeFlowSpec
{
    private readonly ITestOutputHelper _output;

    public CanonRuntimeFlowSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Canonization_pipeline_persists_canonical_and_indexes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureServices(services);
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<ICanonRuntime>();
        var persistence = scope.ServiceProvider.GetRequiredService<ICanonPersistence>() as InMemoryCanonPersistence
            ?? throw new InvalidOperationException("In-memory persistence not registered");

        var executionId = Guid.CreateVersion7();
        var result = await ExecuteCanonization(runtime, executionId, CanonStageBehavior.Immediate, "integration");

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
    }

    [Fact]
    public async Task Stage_only_requests_create_stage_entries_without_persisting_canonical()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureServices(services);
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<ICanonRuntime>();
        var persistence = scope.ServiceProvider.GetRequiredService<ICanonPersistence>() as InMemoryCanonPersistence
            ?? throw new InvalidOperationException("In-memory persistence not registered");

        var executionId = Guid.CreateVersion7();
        var result = await ExecuteCanonization(runtime, executionId, CanonStageBehavior.StageOnly, "queue");

        result.Outcome.Should().Be(CanonizationOutcome.Parked);
        result.Events.Should().ContainSingle(evt => evt.Phase == CanonPipelinePhase.Intake);
        persistence.StageRecords.Should().HaveCount(1);
        persistence.CanonicalEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Stage_only_requests_forward_tags_to_stage_metadata()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureServices(services);
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<ICanonRuntime>();
        var persistence = scope.ServiceProvider.GetRequiredService<ICanonPersistence>() as InMemoryCanonPersistence
            ?? throw new InvalidOperationException("In-memory persistence not registered");

        var executionId = Guid.CreateVersion7();
        var overrideOptions = CanonizationOptions.Default.WithTag("tenant", "acme");
        await ExecuteCanonization(runtime, executionId, CanonStageBehavior.StageOnly, "tagged", overrideOptions);

        persistence.StageRecords.Should().NotBeEmpty();
        var stage = persistence.StageRecords.Last();
        var nonNullStage = stage ?? throw new InvalidOperationException("Expected stage record.");
        Dictionary<string, string?> metadata = nonNullStage.Metadata!;
        if (!metadata.TryGetValue("tenant", out var tenantRaw))
        {
            throw new InvalidOperationException("Expected tenant metadata.");
        }

        string tenant = tenantRaw ?? throw new InvalidOperationException("Expected tenant metadata.");
        tenant.Should().Be("acme");

        if (!metadata.TryGetValue("runtime:stage-behavior", out var behaviorRaw))
        {
            throw new InvalidOperationException("Expected stage behavior metadata.");
        }

        string behavior = behaviorRaw ?? throw new InvalidOperationException("Expected stage behavior metadata.");
        behavior.Should().Be(CanonStageBehavior.StageOnly.ToString());
    }

    [Fact]
    public async Task Replay_returns_canonization_records_in_phase_order()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureServices(services);
        await using var sp = services.BuildServiceProvider();

        ICanonRuntime runtime;
        using (var scope = sp.CreateScope())
        {
            runtime = scope.ServiceProvider.GetRequiredService<ICanonRuntime>();
        }

        var executionId = Guid.CreateVersion7();
        await ExecuteCanonization(runtime, executionId, CanonStageBehavior.Immediate, "replay-1");
        await ExecuteCanonization(runtime, executionId, CanonStageBehavior.Immediate, "replay-2");

        var records = new List<CanonizationRecord>();
        await foreach (var record in runtime.Replay(cancellationToken: CancellationToken.None))
        {
            records.Add(record);
        }

        records.Should().NotBeEmpty();
        records.Should().BeInAscendingOrder(r => r.OccurredAt);
        records.Should().AllSatisfy(record => record.Event.Should().NotBeNull());
        var phases = records.Select(r => r.Event!.Phase).ToList();
        phases.Should().OnlyContain(phase => Enum.IsDefined(typeof(CanonPipelinePhase), phase));
    }

    private static void ConfigureServices(IServiceCollection services)
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

    private static async ValueTask<CanonizationResult<ContactCanon>> ExecuteCanonization(ICanonRuntime runtime, Guid executionId, CanonStageBehavior behavior, string origin, CanonizationOptions? overrideOptions = null)
    {
        var email = $"{origin}-{executionId:N}@example.com";

        var entity = new ContactCanon
        {
            Email = email,
            PhoneNumber = "555-0100",
            DisplayName = "Alpha"
        };

        var options = CanonizationOptions.Default
            .WithOrigin(origin)
            .WithStageBehavior(behavior);

        if (overrideOptions is not null)
        {
            options = CanonizationOptions.Merge(overrideOptions, options);
        }

        return await runtime.Canonize(entity, options, CancellationToken.None).ConfigureAwait(false);
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
