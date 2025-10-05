using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using System.Linq;
using Xunit;

namespace Koan.Canon.Domain.Tests;

public class CanonRuntimeTests
{
    [Fact]
    public async Task Canonize_ShouldRunPipelineAndPersistEntity()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.SetItem("intake", true);
                    return ValueTask.CompletedTask;
                }, "Intake complete");

                pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) =>
                {
                    context.Metadata.SetTag("validated", "true");
                    return ValueTask.FromResult<CanonizationEvent?>(new CanonizationEvent
                    {
                        Message = "Validation executed."
                    });
                });
            })
            .Build();

        var entity = new TestCanonEntity { Name = "runtime-test" };

        var result = await runtime.Canonize(entity);

        persistence.CanonicalSaveCount.Should().Be(1);
        persistence.StageSaveCount.Should().Be(0);
        result.Outcome.Should().Be(CanonizationOutcome.Canonized);
        result.Events.Should().HaveCount(2);
        result.Events.Should().OnlyContain(evt => evt.CanonState != null && evt.CanonState.Lifecycle == CanonLifecycle.Active);
        result.Metadata.Tags.Should().ContainKey("validated");
        entity.Metadata.HasCanonicalId.Should().BeTrue();
    }

    [Fact]
    public async Task Canonize_WithStageOnly_ShouldPersistStageAndPark()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(_ => { })
            .Build();

        var entity = new TestCanonEntity();
        var options = CanonizationOptions.Default.WithStageBehavior(CanonStageBehavior.StageOnly);

        var result = await runtime.Canonize(entity, options);

        persistence.StageSaveCount.Should().Be(1);
        persistence.CanonicalSaveCount.Should().Be(0);
        result.Outcome.Should().Be(CanonizationOutcome.Parked);
        result.DistributionSkipped.Should().BeTrue();
        result.Events.Should().ContainSingle();
        result.Events.Single().CanonState.Should().NotBeNull();
    }

    [Fact]
    public async Task Canonize_ShouldNotifyObservers()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Entity.Metadata.SetOrigin("unit-test");
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var observer = new CollectingObserver();
        using var _ = runtime.RegisterObserver(observer);

        await runtime.Canonize(new TestCanonEntity());

        observer.BeforePhases.Should().ContainSingle(phase => phase == CanonPipelinePhase.Intake);
        observer.AfterPhases.Should().ContainSingle(phase => phase == CanonPipelinePhase.Intake);
        observer.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Replay_ShouldRespectCapacity()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .SetRecordCapacity(2)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) => ValueTask.CompletedTask);
            })
            .Build();

        await runtime.Canonize(new TestCanonEntity());
        await runtime.Canonize(new TestCanonEntity());
        await runtime.Canonize(new TestCanonEntity());

        var records = new List<CanonizationRecord>();
        await foreach (var record in runtime.Replay())
        {
            records.Add(record);
        }

        records.Should().HaveCount(2);
        records.Should().OnlyContain(record => record.Outcome == CanonizationOutcome.Canonized);
    }

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeCanonPersistence : ICanonPersistence
    {
        public int CanonicalSaveCount { get; private set; }
        public int StageSaveCount { get; private set; }

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            CanonicalSaveCount++;
            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            StageSaveCount++;
            return Task.FromResult(stage);
        }
    }

    private sealed class CollectingObserver : ICanonPipelineObserver
    {
        public List<CanonPipelinePhase> BeforePhases { get; } = new();
        public List<CanonPipelinePhase> AfterPhases { get; } = new();
        public List<Exception> Errors { get; } = new();

        public ValueTask BeforePhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CancellationToken cancellationToken = default)
        {
            BeforePhases.Add(phase);
            return ValueTask.CompletedTask;
        }

        public ValueTask AfterPhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CanonizationEvent @event, CancellationToken cancellationToken = default)
        {
            AfterPhases.Add(phase);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(CanonPipelinePhase phase, ICanonPipelineContext context, Exception exception, CancellationToken cancellationToken = default)
        {
            Errors.Add(exception);
            return ValueTask.CompletedTask;
        }
    }
}
