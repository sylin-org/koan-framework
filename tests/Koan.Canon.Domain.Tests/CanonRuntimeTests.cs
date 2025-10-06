using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Domain.Audit;
using Koan.Canon.Domain.Annotations;
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
        result.Events.Should().HaveCount(4);
        result.Events.Select(evt => evt.Phase).Should().Equal(new[]
        {
            CanonPipelinePhase.Intake,
            CanonPipelinePhase.Validation,
            CanonPipelinePhase.Aggregation,
            CanonPipelinePhase.Policy
        });
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
        var options = (CanonizationOptions.Default
            .WithStageBehavior(CanonStageBehavior.StageOnly)
            .WithOrigin("stage-test")
            .WithTag("priority", "high"))
            with { CorrelationId = "corr-123" };

        var result = await runtime.Canonize(entity, options);

        persistence.StageSaveCount.Should().Be(1);
        persistence.CanonicalSaveCount.Should().Be(0);
        result.Outcome.Should().Be(CanonizationOutcome.Parked);
        result.DistributionSkipped.Should().BeTrue();
        result.Events.Should().ContainSingle();
        result.Events.Single().CanonState.Should().NotBeNull();

        var stage = persistence.GetLastStage<TestCanonEntity>();
        stage.Should().NotBeNull();
        stage!.Origin.Should().Be("stage-test");
        stage.CorrelationId.Should().Be("corr-123");
        stage.Metadata.Should().ContainKey("priority").WhoseValue.Should().Be("high");
        stage.Metadata.Should().ContainKey("runtime:stage-behavior").WhoseValue.Should().Be(CanonStageBehavior.StageOnly.ToString());
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

        observer.BeforePhases.Should().Equal(new[]
        {
            CanonPipelinePhase.Intake,
            CanonPipelinePhase.Aggregation,
            CanonPipelinePhase.Policy
        });

        observer.AfterPhases.Should().Equal(new[]
        {
            CanonPipelinePhase.Intake,
            CanonPipelinePhase.Aggregation,
            CanonPipelinePhase.Policy
        });

        observer.AfterEvents.Select(evt => evt.Phase).Should().Equal(new[]
        {
            CanonPipelinePhase.Intake,
            CanonPipelinePhase.Aggregation,
            CanonPipelinePhase.Policy
        });

        observer.AfterEvents.Should().OnlyContain(evt => evt.Message.ToUpperInvariant().Contains("COMPLETED"));
        observer.InvocationOrder.Should().Equal(new[]
        {
            "before:Intake",
            "after:Intake",
            "before:Aggregation",
            "after:Aggregation",
            "before:Policy",
            "after:Policy"
        });
        observer.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Canonize_WithFullPipeline_ShouldEmitProjectionAndDistributionEvents()
    {
        var persistence = new FakeCanonPersistence();
        var distributionExecuted = false;

        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Metadata.SetTag("intake", "true");
                    return ValueTask.CompletedTask;
                }, "Intake step executed");

                pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) => ValueTask.CompletedTask, "Validation step executed");
                pipeline.AddStep(CanonPipelinePhase.Aggregation, (context, _) => ValueTask.CompletedTask, "Aggregation step executed");
                pipeline.AddStep(CanonPipelinePhase.Policy, (context, _) => ValueTask.CompletedTask, "Policy step executed");

                pipeline.AddStep(CanonPipelinePhase.Projection, (context, _) =>
                {
                    context.Metadata.SetTag("projected", "true");
                    return ValueTask.CompletedTask;
                }, "Projection executed.");

                pipeline.AddStep(CanonPipelinePhase.Distribution, (context, _) =>
                {
                    distributionExecuted = true;
                    context.Metadata.SetTag("distributed", "true");
                    return ValueTask.CompletedTask;
                }, "Distribution step executed");
            })
            .Build();

        var result = await runtime.Canonize(new TestCanonEntity { Name = "full" });

        distributionExecuted.Should().BeTrue();
        result.Metadata.Tags.Should().ContainKey("projected").WhoseValue.Should().Be("true");
        result.Metadata.Tags.Should().ContainKey("distributed").WhoseValue.Should().Be("true");
        result.Events.Select(evt => evt.Phase).Should().Equal(new[]
        {
            CanonPipelinePhase.Intake,
            CanonPipelinePhase.Validation,
            CanonPipelinePhase.Aggregation,
            CanonPipelinePhase.Policy,
            CanonPipelinePhase.Projection,
            CanonPipelinePhase.Distribution
        });
        result.Events.Should().Contain(evt => evt.Phase == CanonPipelinePhase.Projection && evt.Message == "Projection executed.");
    }

    [Fact]
    public async Task Canonize_WithSkipDistributionOption_ShouldSetDistributionSkippedFlag()
    {
        var persistence = new FakeCanonPersistence();
        var distributionInvocations = 0;

        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Distribution, (context, _) =>
                {
                    distributionInvocations++;
                    return ValueTask.CompletedTask;
                }, "Distribution executed");
            })
            .Build();

        var normal = await runtime.Canonize(new TestCanonEntity());
        normal.DistributionSkipped.Should().BeFalse();
        distributionInvocations.Should().Be(1);

        distributionInvocations = 0;

        var skipped = await runtime.Canonize(
            new TestCanonEntity(),
            CanonizationOptions.Default with { SkipDistribution = true });

        skipped.DistributionSkipped.Should().BeTrue();
        distributionInvocations.Should().Be(1);
    }

    [Fact]
    public async Task Canonize_WithRequestedViews_ShouldTriggerReprojection()
    {
        var persistence = new FakeCanonPersistence();
        string[]? observedRequestedViews = null;

        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Projection, (context, _) =>
                {
                    observedRequestedViews = context.Options.RequestedViews;
                    return ValueTask.CompletedTask;
                }, "Projection executed");

                pipeline.AddStep(CanonPipelinePhase.Distribution, (context, _) => ValueTask.CompletedTask, "Distribution executed");
            })
            .Build();

        var options = CanonizationOptions.Default.WithRequestedViews("summary", "detail");
        var result = await runtime.Canonize(new TestCanonEntity(), options);

        result.ReprojectionTriggered.Should().BeTrue();
        observedRequestedViews.Should().NotBeNull();
        observedRequestedViews!.Should().BeEquivalentTo(new[] { "summary", "detail" });
        result.Events.Should().Contain(evt => evt.Phase == CanonPipelinePhase.Projection);
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

    [Fact]
    public async Task Replay_ShouldHonorTimeWindowFilters()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) => ValueTask.CompletedTask);
            })
            .Build();

        await runtime.Canonize(new TestCanonEntity());
        await runtime.Canonize(new TestCanonEntity());
        await runtime.Canonize(new TestCanonEntity());

        var allRecords = new List<CanonizationRecord>();
        await foreach (var record in runtime.Replay())
        {
            allRecords.Add(record);
        }

        allRecords.Should().HaveCount(9);

        var middleTimestamp = allRecords[1].OccurredAt;
        var fromFiltered = new List<CanonizationRecord>();
        await foreach (var record in runtime.Replay(from: middleTimestamp))
        {
            fromFiltered.Add(record);
        }

        var expectedFromCount = allRecords.Count(record => record.OccurredAt >= middleTimestamp);
        fromFiltered.Should().HaveCount(expectedFromCount);
        fromFiltered.Min(r => r.OccurredAt).Should().BeOnOrAfter(middleTimestamp);

        var toFiltered = new List<CanonizationRecord>();
        await foreach (var record in runtime.Replay(to: middleTimestamp))
        {
            toFiltered.Add(record);
        }

        var expectedToCount = allRecords.Count(record => record.OccurredAt <= middleTimestamp);
        toFiltered.Should().HaveCount(expectedToCount);
        toFiltered.Max(r => r.OccurredAt).Should().BeOnOrBefore(middleTimestamp);
    }

    [Fact]
    public async Task Canonize_ShouldRespectCustomOutcome()
    {
        var persistence = new FakeCanonPersistence();
        CanonizationOptions? observedOptions = null;

        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.SetItem("canon:outcome", CanonizationOutcome.NoOp);
                    observedOptions = context.Options;
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var result = await runtime.Canonize(new TestCanonEntity());

        result.Outcome.Should().Be(CanonizationOutcome.NoOp);
        observedOptions.Should().NotBeNull();
        observedOptions!.Origin.Should().BeNull();
        persistence.CanonicalSaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Canonize_WithMultipleSources_ShouldMergeExternalIdentities()
    {
        var persistence = new AggregatingCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<DeviceCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    var device = context.Entity;

                    var origin = context.Options.Origin ?? device.SourceKey;
                    if (!string.IsNullOrWhiteSpace(origin))

                    {
                        context.Metadata.SetOrigin(origin!);
                    }

                    context.Metadata.RecordSource(device.SourceKey, source =>
                    {
                        source.SetAttribute("nativeId", device.SourceId);
                    });

                    context.Metadata.RecordExternalId($"identity.external.{device.SourceKey}", device.SourceId, device.SourceKey);
                    return ValueTask.CompletedTask;
                });

                pipeline.AddStep(CanonPipelinePhase.Aggregation, (context, _) =>
                {
                    var device = context.Entity;
                    if (string.IsNullOrWhiteSpace(device.SerialNumber))
                    {
                        throw new InvalidOperationException("Serial number is required for aggregation.");
                    }

                    var existing = persistence.FindBySerial(device.SerialNumber);
                    if (existing is not null)
                    {
                        var mergedMetadata = existing.Metadata.Clone();
                        mergedMetadata.Merge(context.Metadata, preferIncoming: true);
                        mergedMetadata.AssignCanonicalId(existing.Id);

                        context.ApplyMetadata(mergedMetadata);
                        context.Entity.Id = existing.Id;
                    }
                    else
                    {
                        context.Metadata.RecordExternalId("identity.aggregation.serial", device.SerialNumber, "aggregation");
                    }

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var first = new DeviceCanon
        {
            SerialNumber = "SN-123",
            SourceKey = "source1",
            SourceId = "id1"
        };

        var firstResult = await runtime.Canonize(first, CanonizationOptions.Default.WithOrigin("source1"));

        firstResult.Metadata.TryGetExternalId("identity.external.source1", out var primaryExternal).Should().BeTrue();
        primaryExternal!.Value.Should().Be("id1");
        firstResult.Metadata.TryGetExternalId("identity.external.source2", out _).Should().BeFalse();

        var second = new DeviceCanon
        {
            SerialNumber = "SN-123",
            SourceKey = "source2",
            SourceId = "id2"
        };

        var secondResult = await runtime.Canonize(second, CanonizationOptions.Default.WithOrigin("source2"));

        secondResult.Canonical.Id.Should().Be(firstResult.Canonical.Id);
        secondResult.Metadata.TryGetExternalId("identity.external.source1", out var preservedExternal).Should().BeTrue();
        preservedExternal!.Value.Should().Be("id1");
        secondResult.Metadata.TryGetExternalId("identity.external.source2", out var mergedExternal).Should().BeTrue();
        mergedExternal!.Value.Should().Be("id2");

        persistence.CanonicalSaveCount.Should().Be(2);
        var stored = persistence.FindBySerial("SN-123");
        stored.Should().NotBeNull();
        stored!.Metadata.TryGetExternalId("identity.external.source1", out _).Should().BeTrue();
        stored.Metadata.TryGetExternalId("identity.external.source2", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Canonize_WhenContributorThrows_ShouldNotifyErrorAndRecordFailure()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) => throw new InvalidOperationException("boom"));
            })
            .Build();

        var observer = new CollectingObserver();
        using var _ = runtime.RegisterObserver(observer);

        var act = async () => await runtime.Canonize(new TestCanonEntity());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        observer.Errors.Should().ContainSingle(ex => ex is InvalidOperationException);
        persistence.CanonicalSaveCount.Should().Be(0);

        var records = new List<CanonizationRecord>();
        await foreach (var record in runtime.Replay())
        {
            records.Add(record);
        }

        records.Should().ContainSingle();
        var failure = records.Single();
        failure.Outcome.Should().Be(CanonizationOutcome.Failed);
        failure.Event.Should().NotBeNull();
        failure.Event!.Message.ToUpperInvariant().Should().Contain("FAILED");
        failure.Event.Detail.Should().Contain("boom");
    }

    [Fact]
    public async Task ConfigureDefaultOptions_ShouldMergeIntoRequestOptions()
    {
        var persistence = new FakeCanonPersistence();
        CanonizationOptions? observed = null;

        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigureDefaultOptions(options => options.WithOrigin("default-origin").WithTag("source", "defaults"))
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    observed = context.Options;
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var requestOptions = CanonizationOptions.Default.WithTag("priority", "request");
        await runtime.Canonize(new TestCanonEntity(), requestOptions);

        observed.Should().NotBeNull();
        observed!.Origin.Should().Be("default-origin");
        observed.Tags.Should().ContainKey("source").WhoseValue.Should().Be("defaults");
        observed.Tags.Should().ContainKey("priority").WhoseValue.Should().Be("request");
    }

    [Fact]
    public async Task Canonize_WithCompositeAggregationKey_ShouldPersistCompositeIndex()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<CompositeDeviceCanon>(_ => { })
            .Build();

        var entity = new CompositeDeviceCanon
        {
            TenantId = "tenant-42",
            DeviceId = "device-007",
            Name = "laser"
        };

        await runtime.Canonize(entity);

        var compositeKey = "TenantId=tenant-42|DeviceId=device-007";
        var index = persistence.FindIndex<CompositeDeviceCanon>(compositeKey);

        index.Should().NotBeNull();
        index!.CanonicalId.Should().Be(entity.Id);
    }

    [Fact]
    public async Task Canonize_WithCompositeAggregationKey_ShouldReuseCanonicalId()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<CompositeDeviceCanon>(_ => { })
            .Build();

        var first = new CompositeDeviceCanon
        {
            TenantId = "tenant-42",
            DeviceId = "device-007",
            Name = "laser"
        };

        var firstResult = await runtime.Canonize(first, CanonizationOptions.Default with { CorrelationId = "run-1" });

        var second = new CompositeDeviceCanon
        {
            TenantId = "tenant-42",
            DeviceId = "device-007",
            Name = "laser-updated"
        };

        var secondResult = await runtime.Canonize(second, CanonizationOptions.Default with { CorrelationId = "run-2" });

        secondResult.Canonical.Id.Should().Be(firstResult.Canonical.Id);
        secondResult.Metadata.CanonicalId.Should().Be(firstResult.Metadata.CanonicalId);
        secondResult.Metadata.PropertyFootprints.Should().ContainKey(nameof(CompositeDeviceCanon.Name));
        var footprint = secondResult.Metadata.PropertyFootprints[nameof(CompositeDeviceCanon.Name)];
        footprint.Value.Should().Be("laser-updated");
        footprint.ArrivalToken.Should().Be("run-2");
    }

    [Fact]
    public async Task Canonize_WithMissingAggregationKey_ShouldThrow()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<CompositeDeviceCanon>(_ => { })
            .Build();

        var entity = new CompositeDeviceCanon
        {
            TenantId = "tenant-42",
            DeviceId = null,
            Name = "laser"
        };

        var act = () => runtime.Canonize(entity);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Aggregation key property 'DeviceId'*null value*");
    }

    [Fact]
    public async Task Replay_ShouldReturnRecordsInChronologicalOrder()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) => ValueTask.CompletedTask);
            })
            .Build();

        await runtime.Canonize(new TestCanonEntity { Name = "first" }, CanonizationOptions.Default with { CorrelationId = "1" });
        await Task.Delay(5);
        await runtime.Canonize(new TestCanonEntity { Name = "second" }, CanonizationOptions.Default with { CorrelationId = "2" });
        await Task.Delay(5);
        await runtime.Canonize(new TestCanonEntity { Name = "third" }, CanonizationOptions.Default with { CorrelationId = "3" });

        var records = new List<CanonizationRecord>();
        await foreach (var record in runtime.Replay())
        {
            records.Add(record);
        }

    records.Should().HaveCount(9);
        records.Select(r => r.OccurredAt).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Canonize_ShouldPersistAggregationIndexWithSourceAttributes()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Metadata.SetOrigin("contact-sync");
                    context.Metadata.RecordSource("crm", source =>
                    {
                        source.DisplayName = "CRM";
                        source.Channel = "bulk";
                        source.SetAttribute("region", "emea");
                        source.SetAttribute("nativeId", context.Entity.ExternalId);
                    });

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var entity = new TestCanonEntity { Name = "indexed" };
        var options = CanonizationOptions.Default with { CorrelationId = "qa-correlation" };

        var result = await runtime.Canonize(entity, options);

        result.Metadata.CanonicalId.Should().NotBeNullOrWhiteSpace();

        var key = $"ExternalId={entity.ExternalId}";
        var index = persistence.FindIndex<TestCanonEntity>(key);

        index.Should().NotBeNull();
        index!.Kind.Should().Be(CanonIndexKeyKind.Aggregation);
        index.CanonicalId.Should().Be(result.Canonical.Id);
        index.Origin.Should().Be("contact-sync");
        index.Attributes.Should().ContainKey("arrivalToken").WhoseValue.Should().Be("qa-correlation");
        index.Attributes.Should().ContainKey("source").WhoseValue.Should().Be("contact-sync");
        index.Attributes.Should().ContainKey("source.crm.displayName").WhoseValue.Should().Be("CRM");
        index.Attributes.Should().ContainKey("source.crm.channel").WhoseValue.Should().Be("bulk");
        index.Attributes.Should().ContainKey("source.crm.attr.region").WhoseValue.Should().Be("emea");
        index.Attributes.Should().ContainKey("source.crm.attr.nativeId").WhoseValue.Should().Be(entity.ExternalId);
        index.Attributes.Should().ContainKey("source.crm.seenAt").WhoseValue.Should().NotBeNull();

        DateTimeOffset.TryParse(index.Attributes["source.crm.seenAt"], out var seenAt).Should().BeTrue();
        seenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Canonize_WithExistingAggregationKey_ShouldReuseCanonicalId()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Metadata.SetOrigin("dedupe");
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var first = new TestCanonEntity { Name = "first" };
        var firstResult = await runtime.Canonize(first, CanonizationOptions.Default with { CorrelationId = "first-run" });

        var second = new TestCanonEntity { ExternalId = first.ExternalId, Name = "second" };
        var secondResult = await runtime.Canonize(second, CanonizationOptions.Default with { CorrelationId = "second-run" });

        secondResult.Canonical.Id.Should().Be(firstResult.Canonical.Id);
        secondResult.Metadata.CanonicalId.Should().Be(firstResult.Metadata.CanonicalId);

        var indexKey = $"ExternalId={first.ExternalId}";
        var index = persistence.FindIndex<TestCanonEntity>(indexKey);

        index.Should().NotBeNull();
        index!.CanonicalId.Should().Be(firstResult.Canonical.Id);
        index.Attributes.Should().ContainKey("arrivalToken").WhoseValue.Should().Be("second-run");

        secondResult.Metadata.PropertyFootprints.Should().ContainKey(nameof(TestCanonEntity.Name));
        var footprint = secondResult.Metadata.PropertyFootprints[nameof(TestCanonEntity.Name)];
        footprint.Value.Should().Be("second");
        footprint.ArrivalToken.Should().Be("second-run");
        footprint.SourceKey.Should().Be("dedupe");
        footprint.Policy.Should().Be(AggregationPolicyKind.Latest.ToString());
    }

    [Fact]
    public async Task Canonize_WhenAuditingEnabled_ShouldRecordPolicyFootprintsAndAuditEntries()
    {
        var persistence = new FakeCanonPersistence();
        var auditSink = new RecordingAuditSink();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(auditSink)
            .ConfigurePipeline<AuditedCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    var entity = context.Entity;
                    context.Metadata.SetOrigin(entity.SourceSystem);
                    context.Metadata.RecordSource(entity.SourceSystem, source =>
                    {
                        source.DisplayName = $"{entity.SourceSystem}-display";
                        source.Channel = "ingest";
                        source.SetAttribute("payloadId", entity.PayloadId);
                    });

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var payload = new AuditedCanon
        {
            SourceSystem = "orders",
            PayloadId = "doc-1",
            Status = "Pending"
        };

        var result = await runtime.Canonize(payload, CanonizationOptions.Default with { CorrelationId = "audit-run" });

        result.Metadata.PropertyFootprints.Should().ContainKey(nameof(AuditedCanon.Status));
        var footprint = result.Metadata.PropertyFootprints[nameof(AuditedCanon.Status)];
        footprint.Value.Should().Be("Pending");
        footprint.ArrivalToken.Should().Be("audit-run");
        footprint.SourceKey.Should().Be("orders");
        footprint.Policy.Should().Be(AggregationPolicyKind.Latest.ToString());
        footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("incoming");

        var policyKey = $"{nameof(AuditedCanon.Status)}:{AggregationPolicyKind.Latest}";
        result.Metadata.Policies.Should().ContainKey(policyKey);
        var policy = result.Metadata.Policies[policyKey];
        policy.Outcome.Should().Be("incoming");
        policy.Evidence.Should().ContainKey("arrivalToken").WhoseValue.Should().Be("audit-run");

        auditSink.Entries.Should().ContainSingle();
        var entry = auditSink.Entries.Single();
        entry.CanonicalId.Should().Be(result.Canonical.Id);
        entry.EntityType.Should().Be(typeof(AuditedCanon).FullName);
        entry.Property.Should().Be(nameof(AuditedCanon.Status));
        entry.CurrentValue.Should().Be("Pending");
        entry.PreviousValue.Should().BeNull();
        entry.Policy.Should().Be(AggregationPolicyKind.Latest.ToString());
        entry.Source.Should().Be("orders");
        entry.ArrivalToken.Should().Be("audit-run");
        entry.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("incoming");
    }

    [Fact]
    public async Task Canonize_WhenAuditDisabled_ShouldSkipAuditSink()
    {
        var persistence = new FakeCanonPersistence();
        var auditSink = new RecordingAuditSink();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(auditSink)
            .ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Entity.Name = "updated";
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        await runtime.Canonize(new TestCanonEntity(), CanonizationOptions.Default with { CorrelationId = "no-audit" });

        auditSink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Canonize_WhenAuditSinkThrows_ShouldPropagateException()
    {
        var persistence = new FakeCanonPersistence();
        var auditSink = new FailingAuditSink();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(auditSink)
            .ConfigurePipeline<AuditedCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Entity.Status = "Processed";
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var act = () => runtime.Canonize(new AuditedCanon(), CanonizationOptions.Default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("audit-failure");
    }

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
        [AggregationKey]
        public string ExternalId { get; set; } = Guid.NewGuid().ToString("n");

        [AggregationPolicy]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DeviceCanon : CanonEntity<DeviceCanon>
    {
        [AggregationKey]
        public string SerialNumber { get; set; } = string.Empty;

        public string SourceKey { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;

        [AggregationPolicy(AggregationPolicyKind.First)]
        public string? DisplayName { get; set; }
    }

    [Canon(audit: true)]
    private sealed class AuditedCanon : CanonEntity<AuditedCanon>
    {
        [AggregationKey]
        public string ExternalId { get; set; } = Guid.NewGuid().ToString("n");

        public string SourceSystem { get; set; } = string.Empty;

        public string PayloadId { get; set; } = string.Empty;

        [AggregationPolicy(AggregationPolicyKind.Latest)]
        public string? Status { get; set; }
    }

    private sealed class CompositeDeviceCanon : CanonEntity<CompositeDeviceCanon>
    {
        [AggregationKey]
        public string? TenantId { get; set; }

        [AggregationKey]
        public string? DeviceId { get; set; }

        [AggregationPolicy(AggregationPolicyKind.Latest)]
        public string? Name { get; set; }
    }

    private sealed class FakeCanonPersistence : ICanonPersistence
    {
        public int CanonicalSaveCount { get; private set; }
        public int StageSaveCount { get; private set; }
        public object? LastStage { get; private set; }
        public object? LastCanonical { get; private set; }
        private readonly Dictionary<(string EntityType, string Key), CanonIndex> _indices = new();

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            CanonicalSaveCount++;
            LastCanonical = entity;
            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            StageSaveCount++;
            LastStage = stage;
            return Task.FromResult(stage);
        }

        public CanonStage<TModel>? GetLastStage<TModel>() where TModel : CanonEntity<TModel>, new()
            => LastStage as CanonStage<TModel>;

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
        {
            _indices.TryGetValue((entityType, key), out var index);
            return Task.FromResult(index);
        }

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
        {
            _indices[(index.EntityType, index.Key)] = index;
            return Task.CompletedTask;
        }

        public CanonIndex? FindIndex<TModel>(string key) where TModel : CanonEntity<TModel>, new()
        {
            var entityType = typeof(TModel).FullName ?? typeof(TModel).Name;
            return _indices.TryGetValue((entityType, key), out var index) ? index : null;
        }
    }

    private sealed class AggregatingCanonPersistence : ICanonPersistence
    {
        private readonly Dictionary<string, DeviceCanon> _devices = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string EntityType, string Key), CanonIndex> _indices = new();

        public int CanonicalSaveCount { get; private set; }

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            CanonicalSaveCount++;

            if (entity is DeviceCanon device)
            {
                _devices[device.SerialNumber] = device;
            }

            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(stage);

        public DeviceCanon? FindBySerial(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return null;
            }

            return _devices.TryGetValue(serialNumber, out var device) ? device : null;
        }

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
        {
            _indices.TryGetValue((entityType, key), out var index);
            return Task.FromResult(index);
        }

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
        {
            _indices[(index.EntityType, index.Key)] = index;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditSink : ICanonAuditSink
    {
        private readonly List<CanonAuditEntry> _entries = new();

        public IReadOnlyList<CanonAuditEntry> Entries => _entries;

        public Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
        {
            _entries.AddRange(entries);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingAuditSink : ICanonAuditSink
    {
        public Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
            => throw new InvalidOperationException("audit-failure");
    }

    private sealed class CollectingObserver : ICanonPipelineObserver
    {
        public List<CanonPipelinePhase> BeforePhases { get; } = new();
        public List<CanonPipelinePhase> AfterPhases { get; } = new();
        public List<Exception> Errors { get; } = new();
        public List<CanonizationEvent> AfterEvents { get; } = new();
        public List<string> InvocationOrder { get; } = new();

        public ValueTask BeforePhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CancellationToken cancellationToken = default)
        {
            BeforePhases.Add(phase);
            InvocationOrder.Add($"before:{phase}");
            return ValueTask.CompletedTask;
        }

        public ValueTask AfterPhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CanonizationEvent @event, CancellationToken cancellationToken = default)
        {
            AfterPhases.Add(phase);
            AfterEvents.Add(@event);
            InvocationOrder.Add($"after:{phase}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(CanonPipelinePhase phase, ICanonPipelineContext context, Exception exception, CancellationToken cancellationToken = default)
        {
            Errors.Add(exception);
            return ValueTask.CompletedTask;
        }
    }
}
