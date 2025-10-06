using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Domain.Audit;
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Runtime;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Direct;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Canonize_InstanceShortcut_ShouldResolveRuntimeFromAppHost()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(_ => { })
            .Build();

        using var services = new ServiceCollection()
            .AddSingleton<ICanonRuntime>(runtime)
            .BuildServiceProvider();

        var previous = AppHost.Current;
        try
        {
            AppHost.Current = services;

            var entity = new TestCanonEntity { Name = "shortcut" };

            var result = await entity.Canonize(origin: "hr");

            persistence.CanonicalSaveCount.Should().Be(1);
            result.Metadata.Origin.Should().Be("hr");
        }
        finally
        {
            AppHost.Current = previous;
        }
    }

    [Fact]
    public async Task Canonize_InstanceShortcut_ShouldApplyOptionCustomization()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<TestCanonEntity>(_ => { })
            .Build();

        using var services = new ServiceCollection()
            .AddSingleton<ICanonRuntime>(runtime)
            .BuildServiceProvider();

        var previous = AppHost.Current;
        try
        {
            AppHost.Current = services;

            var entity = new TestCanonEntity { Name = "shortcut-tags" };

            var result = await entity.Canonize(
                origin: "sap",
                configure: opts => opts.WithTag("priority", "high"));

            persistence.CanonicalSaveCount.Should().Be(1);
            result.Metadata.Origin.Should().Be("sap");
            result.Metadata.Tags.Should().ContainKey("priority").WhoseValue.Should().Be("high");
        }
        finally
        {
            AppHost.Current = previous;
        }
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
    public async Task Canonize_WithPartialAggregationKey_ShouldPersistUsingAvailableToken()
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

        var first = await runtime.Canonize(entity);

        first.Metadata.CanonicalId.Should().NotBeNull();
        var index = persistence.FindIndex<CompositeDeviceCanon>("TenantId=tenant-42");
        index.Should().NotBeNull();
        index!.CanonicalId.Should().Be(first.Canonical.Id);

        var followUp = await runtime.Canonize(new CompositeDeviceCanon
        {
            TenantId = "tenant-42",
            DeviceId = null,
            Name = "laser-updated"
        });

        followUp.Canonical.Id.Should().Be(first.Canonical.Id);
    }

    [Fact]
    public async Task Canonize_WithIdentityGraph_ShouldUnionCanonicalIds()
    {
        var persistence = new FakeCanonPersistence();
        using var appHost = persistence.AttachToAppHost();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<PersonIdentityCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(context.Entity.Source))
                    {
                        context.Metadata.SetOrigin(context.Entity.Source!);
                    }

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var first = await runtime.Canonize(new PersonIdentityCanon
        {
            Email = "sam@example.com",
            Username = null,
            EmployeeId = null,
            DisplayName = "Sam",
            Source = "crm"
        });

        var second = await runtime.Canonize(new PersonIdentityCanon
        {
            Email = null,
            Username = "sammy",
            EmployeeId = null,
            DisplayName = "Sammy",
            Source = "support"
        });

        first.Canonical.Id.Should().NotBe(second.Canonical.Id);

        var bridge = await runtime.Canonize(new PersonIdentityCanon
        {
            Email = "sam@example.com",
            Username = "sammy",
            EmployeeId = null,
            DisplayName = "Samuel",
            Source = "hr"
        });

        bridge.Canonical.Id.Should().Be(first.Canonical.Id);
        bridge.Metadata.Tags.Should().ContainKey("identity:merged-from");
        bridge.Metadata.Tags["identity:merged-from"].Split(',').Should().Contain(second.Canonical.Id);

        var usernameOnly = await runtime.Canonize(new PersonIdentityCanon
        {
            Email = null,
            Username = "sammy",
            EmployeeId = null,
            DisplayName = "Sam duplicate",
            Source = "support"
        });

        usernameOnly.Canonical.Id.Should().Be(first.Canonical.Id);

        var emailIndex = persistence.FindIndex<PersonIdentityCanon>("Email=sam@example.com");
        emailIndex.Should().NotBeNull();
        emailIndex!.CanonicalId.Should().Be(first.Canonical.Id);

        var usernameIndex = persistence.FindIndex<PersonIdentityCanon>("Username=sammy");
        usernameIndex.Should().NotBeNull();
        usernameIndex!.CanonicalId.Should().Be(first.Canonical.Id);
    }

    [Fact]
    public async Task Canonize_WithIdentityGraph_ShouldRecordLineageAndNormalizeIndexes()
    {
        var persistence = new FakeCanonPersistence();
        using var appHost = persistence.AttachToAppHost();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<PersonIdentityCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(context.Entity.Source))
                    {
                        context.Metadata.SetOrigin(context.Entity.Source!);
                    }

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var emailOnly = new PersonIdentityCanon
        {
            Id = "zz-canonical",
            Email = "alpha@example.com",
            Username = null,
            EmployeeId = null,
            DisplayName = "Alpha",
            Source = "crm"
        };

        var usernameOnly = new PersonIdentityCanon
        {
            Id = "AA-canonical",
            Email = null,
            Username = "alpha-user",
            EmployeeId = null,
            DisplayName = "User Alpha",
            Source = "support"
        };

        var emailResult = await runtime.Canonize(emailOnly, CanonizationOptions.Default with { CorrelationId = "run-email" });
        var usernameResult = await runtime.Canonize(usernameOnly, CanonizationOptions.Default with { CorrelationId = "run-username" });

        emailResult.Canonical.Id.Should().Be("zz-canonical");
        usernameResult.Canonical.Id.Should().Be("AA-canonical");

        var bridge = new PersonIdentityCanon
        {
            Id = "bridge-id",
            Email = "alpha@example.com",
            Username = "alpha-user",
            EmployeeId = "emp-42",
            DisplayName = "Alpha Prime",
            Source = "workday"
        };

        var bridgeResult = await runtime.Canonize(bridge, CanonizationOptions.Default with { CorrelationId = "run-bridge" });

        bridgeResult.Canonical.Id.Should().Be("AA-canonical");
        bridgeResult.Metadata.CanonicalId.Should().Be("AA-canonical");
        bridgeResult.Metadata.Tags.Should().ContainKey("identity:merged-from");
        bridgeResult.Metadata.Tags["identity:merged-from"].Split(',').Should().Contain("zz-canonical");

        var lineage = bridgeResult.Metadata.Lineage;
        lineage.Changes.Should().Contain(change => change.Kind == CanonLineageChangeKind.Superseded && change.RelatedId == "zz-canonical");
        lineage.Changes.Should().Contain(change =>
            change.Kind == CanonLineageChangeKind.MetadataUpdated &&
            change.Notes != null &&
            change.Notes.Contains("identity-union:zz-canonical", StringComparison.Ordinal));

        var emailIndex = persistence.FindIndex<PersonIdentityCanon>("Email=alpha@example.com");
        emailIndex.Should().NotBeNull();
        emailIndex!.CanonicalId.Should().Be("AA-canonical");

        var usernameIndex = persistence.FindIndex<PersonIdentityCanon>("Username=alpha-user");
        usernameIndex.Should().NotBeNull();
        usernameIndex!.CanonicalId.Should().Be("AA-canonical");

        var employeeIndex = persistence.FindIndex<PersonIdentityCanon>("EmployeeId=emp-42");
        employeeIndex.Should().NotBeNull();
        employeeIndex!.CanonicalId.Should().Be("AA-canonical");

        var compositeIndex = persistence.FindIndex<PersonIdentityCanon>("Email=alpha@example.com|Username=alpha-user|EmployeeId=emp-42");
        compositeIndex.Should().NotBeNull();
        compositeIndex!.CanonicalId.Should().Be("AA-canonical");

        var repeat = await runtime.Canonize(new PersonIdentityCanon
        {
            Email = "alpha@example.com",
            Username = null,
            EmployeeId = "emp-42",
            DisplayName = "Alpha Repeat",
            Source = "crm"
        }, CanonizationOptions.Default with { CorrelationId = "run-repeat" });

        repeat.Metadata.Tags.Should().ContainKey("identity:merged-from");
        repeat.Metadata.Tags["identity:merged-from"].Split(',', StringSplitOptions.RemoveEmptyEntries).Should().OnlyContain(id => id == "zz-canonical");
    }

    [Fact]
    public async Task Canonize_WithIdentityGraph_WithAllNullKeys_ShouldThrow()
    {
        var persistence = new FakeCanonPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<PersonIdentityCanon>(_ => { })
            .Build();

        var payload = new PersonIdentityCanon
        {
            Email = null,
            Username = null,
            EmployeeId = null,
            DisplayName = "Null Keys"
        };

        var act = () => runtime.Canonize(payload);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires at least one aggregation key value; all declared keys were null or empty*");
    }

    [Fact]
    public async Task Canonize_WithSourceOfTruthPolicy_ShouldHonorAuthoritativeSource()
    {
        var persistence = new FakeCanonPersistence();
        using var appHost = persistence.AttachToAppHost();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<SourceOfTruthPersonCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(context.Entity.Source))
                    {
                        context.Metadata.SetOrigin(context.Entity.Source!);
                    }

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var crm = await runtime.Canonize(new SourceOfTruthPersonCanon
        {
            EmployeeId = "42",
            FullName = "CRM Name",
            Title = "Sales Rep",
            Source = "crm"
        });

        crm.Canonical.FullName.Should().Be("CRM Name");
        var crmFootprint = crm.Metadata.PropertyFootprints[nameof(SourceOfTruthPersonCanon.FullName)];
        crmFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("fallback");
        crmFootprint.Evidence.Should().ContainKey("fallbackPolicy").WhoseValue.Should().Be(AggregationPolicyKind.First.ToString());

        var workday = await runtime.Canonize(new SourceOfTruthPersonCanon
        {
            EmployeeId = "42",
            FullName = "Workday Name",
            Title = "Engineer",
            Source = "workday"
        });

        workday.Canonical.FullName.Should().Be("Workday Name");
        var workdayFootprint = workday.Metadata.PropertyFootprints[nameof(SourceOfTruthPersonCanon.FullName)];
        workdayFootprint.SourceKey.Should().Be("workday");
        workdayFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("incoming");
        workdayFootprint.Evidence.Should().NotContainKey("fallbackPolicy");

        var crmOverride = await runtime.Canonize(new SourceOfTruthPersonCanon
        {
            EmployeeId = "42",
            FullName = "CRM Override",
            Title = "Lead",
            Source = "crm"
        });

        crmOverride.Canonical.FullName.Should().Be("Workday Name");
        var overrideFootprint = crmOverride.Metadata.PropertyFootprints[nameof(SourceOfTruthPersonCanon.FullName)];
        overrideFootprint.SourceKey.Should().Be("workday");
        overrideFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("existing");
        overrideFootprint.Evidence.Should().NotContainKey("fallbackPolicy");
    }

    [Fact]
    public async Task Canonize_WithSourceOfTruthPolicy_ShouldAcceptAnyConfiguredAuthority()
    {
        var persistence = new FakeCanonPersistence();
        using var appHost = persistence.AttachToAppHost();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<MultiAuthorityPersonCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(context.Entity.Source))
                    {
                        context.Metadata.SetOrigin(context.Entity.Source!);
                    }

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var fallback = await runtime.Canonize(new MultiAuthorityPersonCanon
        {
            EmployeeId = "42",
            FullName = "CRM Name",
            Source = "crm"
        }, CanonizationOptions.Default with { CorrelationId = "run-fallback" });

        fallback.Canonical.FullName.Should().Be("CRM Name");
        var fallbackFootprint = fallback.Metadata.PropertyFootprints[nameof(MultiAuthorityPersonCanon.FullName)];
        fallbackFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("fallback");
        fallbackFootprint.Evidence.Should().ContainKey("fallbackPolicy").WhoseValue.Should().Be(AggregationPolicyKind.First.ToString());

        var sap = await runtime.Canonize(new MultiAuthorityPersonCanon
        {
            EmployeeId = "42",
            FullName = "SAP Name",
            Source = "sap"
        }, CanonizationOptions.Default with { CorrelationId = "run-sap" });

        sap.Canonical.FullName.Should().Be("SAP Name");
        var sapFootprint = sap.Metadata.PropertyFootprints[nameof(MultiAuthorityPersonCanon.FullName)];
        sapFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("incoming");
        sapFootprint.Evidence.Should().NotContainKey("fallbackPolicy");

        var workday = await runtime.Canonize(new MultiAuthorityPersonCanon
        {
            EmployeeId = "42",
            FullName = "Workday Name",
            Source = "workday"
        }, CanonizationOptions.Default with { CorrelationId = "run-workday" });

        workday.Canonical.FullName.Should().Be("Workday Name");
        var workdayFootprint = workday.Metadata.PropertyFootprints[nameof(MultiAuthorityPersonCanon.FullName)];
        workdayFootprint.SourceKey.Should().Be("workday");
        workdayFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("incoming");

        var nonAuthorityNull = await runtime.Canonize(new MultiAuthorityPersonCanon
        {
            EmployeeId = "42",
            FullName = "Support Override",
            Source = "support"
        }, CanonizationOptions.Default with { CorrelationId = "run-support" });

        nonAuthorityNull.Canonical.FullName.Should().Be("Workday Name");
        var supportFootprint = nonAuthorityNull.Metadata.PropertyFootprints[nameof(MultiAuthorityPersonCanon.FullName)];
        supportFootprint.SourceKey.Should().Be("workday");
        supportFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("existing");
        supportFootprint.Evidence.Should().NotContainKey("fallbackPolicy");
    }

    [Fact]
    public async Task Canonize_WithSourceOfTruthPolicy_LatestFallback_ShouldRespectArrivalOrdering()
    {
        var persistence = new FakeCanonPersistence();
        using var appHost = persistence.AttachToAppHost();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<LatestFallbackPersonCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(context.Entity.Source))
                    {
                        context.Metadata.SetOrigin(context.Entity.Source!);
                    }

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var first = await runtime.Canonize(new LatestFallbackPersonCanon
        {
            EmployeeId = "777",
            PreferredName = "First",
            Source = "crm"
        }, CanonizationOptions.Default with { CorrelationId = "run-1" });

        var firstFootprint = first.Metadata.PropertyFootprints[nameof(LatestFallbackPersonCanon.PreferredName)];
        firstFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("fallback");
        firstFootprint.Evidence.Should().ContainKey("fallbackPolicy").WhoseValue.Should().Be(AggregationPolicyKind.Latest.ToString());

        var second = await runtime.Canonize(new LatestFallbackPersonCanon
        {
            EmployeeId = "777",
            PreferredName = "Second",
            Source = "marketing"
        }, CanonizationOptions.Default with { CorrelationId = "run-2" });

        second.Canonical.PreferredName.Should().Be("Second");
        var secondFootprint = second.Metadata.PropertyFootprints[nameof(LatestFallbackPersonCanon.PreferredName)];
        secondFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("fallback");
        secondFootprint.Evidence.Should().ContainKey("fallbackPolicy").WhoseValue.Should().Be(AggregationPolicyKind.Latest.ToString());
        secondFootprint.Evidence.Should().ContainKey("incoming").WhoseValue.Should().Be("Second");

        var authoritative = await runtime.Canonize(new LatestFallbackPersonCanon
        {
            EmployeeId = "777",
            PreferredName = "Authoritative",
            Source = "erp"
        }, CanonizationOptions.Default with { CorrelationId = "run-authority" });

        authoritative.Canonical.PreferredName.Should().Be("Authoritative");
        var authoritativeFootprint = authoritative.Metadata.PropertyFootprints[nameof(LatestFallbackPersonCanon.PreferredName)];
        authoritativeFootprint.SourceKey.Should().Be("erp");
        authoritativeFootprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("incoming");
        authoritativeFootprint.Evidence.Should().NotContainKey("fallbackPolicy");
    }

    [Fact]
    public async Task Canonize_WithSourceOfTruthPolicy_ShouldPreserveAuthoritativeValueWhenNonAuthoritySendsNull()
    {
        var persistence = new FakeCanonPersistence();
        using var appHost = persistence.AttachToAppHost();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<SourceOfTruthPersonCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(context.Entity.Source))
                    {
                        context.Metadata.SetOrigin(context.Entity.Source!);
                    }

                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        await runtime.Canonize(new SourceOfTruthPersonCanon
        {
            EmployeeId = "91",
            FullName = "CRM Name",
            Title = "Rep",
            Source = "crm"
        }, CanonizationOptions.Default with { CorrelationId = "run-crm" });

        var authoritative = await runtime.Canonize(new SourceOfTruthPersonCanon
        {
            EmployeeId = "91",
            FullName = "Workday Name",
            Title = "Engineer",
            Source = "workday"
        }, CanonizationOptions.Default with { CorrelationId = "run-workday" });

        var nonAuthorityNull = await runtime.Canonize(new SourceOfTruthPersonCanon
        {
            EmployeeId = "91",
            FullName = null,
            Title = "Support",
            Source = "crm"
        }, CanonizationOptions.Default with { CorrelationId = "run-null" });

        nonAuthorityNull.Canonical.FullName.Should().Be("Workday Name");
        var footprint = nonAuthorityNull.Metadata.PropertyFootprints[nameof(SourceOfTruthPersonCanon.FullName)];
        footprint.SourceKey.Should().Be("workday");
        footprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("existing");
        footprint.Evidence.Should().NotContainKey("fallbackPolicy");
        footprint.Evidence.Should().ContainKey("incoming").WhoseValue.Should().BeNull();
    }

    [Fact]
    public async Task Canonize_WithSourceOfTruthPolicy_WhenAuditingEnabled_ShouldEmitAuthorityEvidence()
    {
        var persistence = new FakeCanonPersistence();
        using var appHost = persistence.AttachToAppHost();
        var auditSink = new RecordingAuditSink();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .UseAuditSink(auditSink)
            .ConfigurePipeline<AuditedSourceOfTruthCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Metadata.SetOrigin(context.Entity.Source ?? "unknown");
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var result = await runtime.Canonize(new AuditedSourceOfTruthCanon
        {
            EmployeeId = "501",
            DisplayName = "Authoritative",
            Source = "workday"
        }, CanonizationOptions.Default with { CorrelationId = "run-audit" });

        var footprint = result.Metadata.PropertyFootprints[nameof(AuditedSourceOfTruthCanon.DisplayName)];
        footprint.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("incoming");
        footprint.Evidence.Should().NotContainKey("fallbackPolicy");

        auditSink.Entries.Should().ContainSingle();
        var entry = auditSink.Entries.Single();
        entry.Policy.Should().Be(AggregationPolicyKind.SourceOfTruth.ToString());
        entry.Evidence.Should().ContainKey("authority").WhoseValue.Should().Be("incoming");
    }

    [Fact]
    public void AggregationMetadata_ShouldExposeFriendlyPolicyHelpers()
    {
        var metadata = CanonModelAggregationMetadata.For<SourceOfTruthPersonCanon>();

        metadata.TryGetPolicy(nameof(SourceOfTruthPersonCanon.FullName), out var fullNameDescriptor).Should().BeTrue();
        fullNameDescriptor.Kind.Should().Be(AggregationPolicyKind.SourceOfTruth);
        fullNameDescriptor.HasAuthoritativeSources.Should().BeTrue();
        fullNameDescriptor.AuthoritativeSources.Should().ContainSingle().Which.Should().Be("workday");
        fullNameDescriptor.Fallback.Should().Be(AggregationPolicyKind.First);

        metadata.GetPolicyOrDefault(nameof(SourceOfTruthPersonCanon.Title))!.Kind.Should().Be(AggregationPolicyKind.Latest);
        metadata.TryGetPolicy(nameof(SourceOfTruthPersonCanon.Source), out _).Should().BeFalse();

        var typedDescriptor = metadata.GetRequiredPolicy<SourceOfTruthPersonCanon, string?>(person => person.FullName);
        typedDescriptor.Kind.Should().Be(AggregationPolicyKind.SourceOfTruth);

        metadata.TryGetPolicy<SourceOfTruthPersonCanon, string?>(person => person.Title, out var titleDescriptor).Should().BeTrue();
        titleDescriptor!.Kind.Should().Be(AggregationPolicyKind.Latest);

        metadata.GetPolicyOrDefault<SourceOfTruthPersonCanon, string?>(person => person.Source).Should().BeNull();
    }

    [Fact]
    public void CanonModelAggregationMetadata_ForInvalidSourceOfTruthConfigurations_ShouldThrow()
    {
        var scenarios = new (Type ModelType, string Expected)[]
        {
            (typeof(BadSourceOfTruthCanonMissingSource), "does not specify any Source or Sources"),
            (typeof(BadSourceOfTruthCanonWithInvalidFallback), "cannot declare SourceOfTruth policy with SourceOfTruth fallback"),
            (typeof(BadNonSourcePolicyCanon), "Sources may only be configured for SourceOfTruth")
        };

        foreach (var (modelType, expectedFragment) in scenarios)
        {
            var act = () => CanonModelAggregationMetadata.For(modelType);
            act.Should().Throw<InvalidOperationException>().WithMessage($"*{expectedFragment}*");
        }
    }

    [Fact]
    public void AggregationMetadataHelpers_ShouldGuardAgainstInvalidUsage()
    {
        var metadata = CanonModelAggregationMetadata.For<SourceOfTruthPersonCanon>();

        Action mismatched = () => metadata.GetRequiredPolicy<CompositeDeviceCanon, string?>(device => device.Name);
        mismatched.Should().Throw<InvalidOperationException>().WithMessage("*cannot be used with model type*");

    Action invalidExpression = () => metadata.TryGetPolicy<SourceOfTruthPersonCanon, string?>(person => person.FullName!.ToLowerInvariant(), out _);
        invalidExpression.Should().Throw<ArgumentException>().WithMessage("*property expression*");

        Action nullMetadata = () => CanonModelAggregationMetadataExtensions.TryGetPolicy<SourceOfTruthPersonCanon, string?>(null!, person => person.FullName, out _);
        nullMetadata.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("metadata");
    }

    [Fact]
    public void CanonRuntimeBuilder_ShouldExposeAggregationPolicyDetailsInMetadata()
    {
        var builder = new CanonRuntimeBuilder()
            .UsePersistence(new FakeCanonPersistence())
            .ConfigurePipeline<SourceOfTruthPersonCanon>(_ => { });

        var configuration = builder.BuildConfiguration();

        configuration.PipelineMetadata.Should().ContainKey(typeof(SourceOfTruthPersonCanon));
        var metadata = configuration.PipelineMetadata[typeof(SourceOfTruthPersonCanon)];
        metadata.AggregationPolicyDetails.Should().ContainKey(nameof(SourceOfTruthPersonCanon.FullName));
        var descriptor = metadata.AggregationPolicyDetails[nameof(SourceOfTruthPersonCanon.FullName)];
        descriptor.Kind.Should().Be(AggregationPolicyKind.SourceOfTruth);
        descriptor.AuthoritativeSources.Should().ContainSingle().Which.Should().Be("workday");
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

    private sealed class PersonIdentityCanon : CanonEntity<PersonIdentityCanon>
    {
        [AggregationKey]
        public string? Email { get; set; }

        [AggregationKey]
        public string? Username { get; set; }

        [AggregationKey]
        public string? EmployeeId { get; set; }

        public string? Source { get; set; }

        [AggregationPolicy(AggregationPolicyKind.Latest)]
        public string? DisplayName { get; set; }
    }

    private sealed class SourceOfTruthPersonCanon : CanonEntity<SourceOfTruthPersonCanon>
    {
        [AggregationKey]
        public string? EmployeeId { get; set; }

        public string? Source { get; set; }

        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "workday", Fallback = AggregationPolicyKind.First)]
        public string? FullName { get; set; }

        [AggregationPolicy(AggregationPolicyKind.Latest)]
        public string? Title { get; set; }
    }

    private sealed class MultiAuthorityPersonCanon : CanonEntity<MultiAuthorityPersonCanon>
    {
        [AggregationKey]
        public string? EmployeeId { get; set; }

        public string? Source { get; set; }

        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Sources = new[] { "workday", "sap" }, Fallback = AggregationPolicyKind.First)]
        public string? FullName { get; set; }
    }

    private sealed class LatestFallbackPersonCanon : CanonEntity<LatestFallbackPersonCanon>
    {
        [AggregationKey]
        public string? EmployeeId { get; set; }

        public string? Source { get; set; }

        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "erp", Fallback = AggregationPolicyKind.Latest)]
        public string? PreferredName { get; set; }
    }

    [Canon(audit: true)]
    private sealed class AuditedSourceOfTruthCanon : CanonEntity<AuditedSourceOfTruthCanon>
    {
        [AggregationKey]
        public string? EmployeeId { get; set; }

        public string? Source { get; set; }

        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "workday", Fallback = AggregationPolicyKind.First)]
        public string? DisplayName { get; set; }
    }

    private sealed class BadSourceOfTruthCanonMissingSource : CanonEntity<BadSourceOfTruthCanonMissingSource>
    {
        [AggregationKey]
        public string? EmployeeId { get; set; }

        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth)]
        public string? FullName { get; set; }
    }

    private sealed class BadSourceOfTruthCanonWithInvalidFallback : CanonEntity<BadSourceOfTruthCanonWithInvalidFallback>
    {
        [AggregationKey]
        public string? EmployeeId { get; set; }

        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "workday", Fallback = AggregationPolicyKind.SourceOfTruth)]
        public string? FullName { get; set; }
    }

    private sealed class BadNonSourcePolicyCanon : CanonEntity<BadNonSourcePolicyCanon>
    {
        [AggregationKey]
        public string? EmployeeId { get; set; }

        [AggregationPolicy(AggregationPolicyKind.First, Source = "workday")]
        public string? FullName { get; set; }
    }

    private sealed class FakeCanonPersistence : ICanonPersistence
    {
        public int CanonicalSaveCount { get; private set; }
        public int StageSaveCount { get; private set; }
        public object? LastStage { get; private set; }
        public object? LastCanonical { get; private set; }

        private readonly Dictionary<(string EntityType, string Key), CanonIndex> _indices = new();
        private readonly Dictionary<(Type EntityType, string Id), object> _canonicals = new();

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            CanonicalSaveCount++;
            LastCanonical = entity;
            _canonicals[(typeof(TModel), entity.Id)] = entity;
            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            if (stage is null)
            {
                throw new ArgumentNullException(nameof(stage));
            }

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
            if (index is null)
            {
                throw new ArgumentNullException(nameof(index));
            }

            _indices[(index.EntityType, index.Key)] = index;
            return Task.CompletedTask;
        }

        public CanonIndex? FindIndex<TModel>(string key) where TModel : CanonEntity<TModel>, new()
        {
            var entityType = typeof(TModel).FullName ?? typeof(TModel).Name;
            return _indices.TryGetValue((entityType, key), out var index) ? index : null;
        }

        public IDisposable AttachToAppHost()
            => new AppHostScope(this);

        private object? TryGet(Type entityType, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return _canonicals.TryGetValue((entityType, id), out var entity) ? entity : null;
        }

        private IReadOnlyList<object> List(Type entityType)
        {
            return _canonicals
                .Where(pair => pair.Key.EntityType == entityType)
                .Select(pair => pair.Value)
                .ToList();
        }

        private int DeleteMany(Type entityType, IEnumerable<string> ids)
        {
            var count = 0;
            foreach (var id in ids)
            {
                if (_canonicals.Remove((entityType, id)))
                {
                    count++;
                }
            }

            return count;
        }

        private int DeleteAll(Type entityType)
        {
            var keys = _canonicals.Keys.Where(pair => pair.EntityType == entityType).ToList();
            for (var i = 0; i < keys.Count; i++)
            {
                _canonicals.Remove(keys[i]);
            }

            return keys.Count;
        }

        private IServiceProvider BuildServiceProvider()
        {
            return new ServiceCollection()
                .AddSingleton<IDataService>(new InMemoryDataService(this))
                .BuildServiceProvider();
        }

        private sealed class AppHostScope : IDisposable
        {
            private readonly IServiceProvider _provider;
            private readonly IServiceProvider? _previous;

            public AppHostScope(FakeCanonPersistence owner)
            {
                _previous = AppHost.Current;
                _provider = owner.BuildServiceProvider();
                AppHost.Current = _provider;
            }

            public void Dispose()
            {
                AppHost.Current = _previous;

                if (_provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private sealed class InMemoryDataService : IDataService
        {
            private readonly FakeCanonPersistence _owner;

            public InMemoryDataService(FakeCanonPersistence owner)
            {
                _owner = owner;
            }

            public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
                where TEntity : class, IEntity<TKey>
                where TKey : notnull
            {
                if (typeof(TKey) != typeof(string))
                {
                    throw new NotSupportedException("The test data service only supports string keys.");
                }

                return new InMemoryRepository<TEntity, TKey>(_owner);
            }

            public IDirectSession Direct(string? source = null, string? adapter = null)
            {
                throw new NotSupportedException("Direct sessions are not available in canon runtime tests.");
            }

            public IVectorSearchRepository<TEntity, TKey>? TryGetVectorRepository<TEntity, TKey>()
                where TEntity : class, IEntity<TKey>
                where TKey : notnull
            {
                return null;
            }
        }

        private sealed class InMemoryRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            private readonly FakeCanonPersistence _owner;

            public InMemoryRepository(FakeCanonPersistence owner)
            {
                _owner = owner;
            }

            public Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
            {
                if (id is not string key)
                {
                    throw new NotSupportedException("The test data service only supports string identifiers.");
                }

                var entity = _owner.TryGet(typeof(TEntity), key) as TEntity;
                return Task.FromResult(entity);
            }

            public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
            {
                var results = _owner.List(typeof(TEntity)).OfType<TEntity>().ToList();
                return Task.FromResult<IReadOnlyList<TEntity>>(results);
            }

            public Task<CountResult> CountAsync(CountRequest<TEntity> request, CancellationToken ct = default)
            {
                var count = _owner.List(typeof(TEntity)).Count;
                return Task.FromResult(CountResult.Exact(count));
            }

            public Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
            {
                if (model is null)
                {
                    throw new ArgumentNullException(nameof(model));
                }

                if (model.Id is null)
                {
                    throw new InvalidOperationException("Models must have an identifier before being stored.");
                }

                if (model.Id is not string key)
                {
                    throw new NotSupportedException("The test data service only supports string identifiers.");
                }

                _owner._canonicals[(typeof(TEntity), key)] = model;
                return Task.FromResult(model);
            }

            public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
            {
                if (id is not string key)
                {
                    throw new NotSupportedException("The test data service only supports string identifiers.");
                }

                var removed = _owner._canonicals.Remove((typeof(TEntity), key));
                return Task.FromResult(removed);
            }

            public Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
            {
                if (models is null)
                {
                    throw new ArgumentNullException(nameof(models));
                }

                var count = 0;
                foreach (var model in models)
                {
                    if (model is null)
                    {
                        continue;
                    }

                    var identifier = model.Id;
                    if (identifier is null)
                    {
                        continue;
                    }

                    if (identifier is not string key)
                    {
                        throw new NotSupportedException("The test data service only supports string identifiers.");
                    }

                    _owner._canonicals[(typeof(TEntity), key)] = model;
                    count++;
                }

                return Task.FromResult(count);
            }

            public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
            {
                if (ids is null)
                {
                    throw new ArgumentNullException(nameof(ids));
                }

                var buffer = new List<string>();
                foreach (var candidate in ids)
                {
                    if (candidate is string key)
                    {
                        buffer.Add(key);
                        continue;
                    }

                    throw new NotSupportedException("The test data service only supports string identifiers.");
                }

                var count = _owner.DeleteMany(typeof(TEntity), buffer);
                return Task.FromResult(count);
            }

            public Task<int> DeleteAllAsync(CancellationToken ct = default)
            {
                var count = _owner.DeleteAll(typeof(TEntity));
                return Task.FromResult(count);
            }

            public Task<long> RemoveAllAsync(RemoveStrategy strategy, CancellationToken ct = default)
            {
                var count = _owner.DeleteAll(typeof(TEntity));
                return Task.FromResult((long)count);
            }

            public IBatchSet<TEntity, TKey> CreateBatch()
            {
                throw new NotSupportedException("Batch operations are not supported in the canon runtime tests.");
            }
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
