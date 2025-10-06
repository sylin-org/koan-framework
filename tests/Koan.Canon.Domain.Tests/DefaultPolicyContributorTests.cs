using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using Xunit;

namespace Koan.Canon.Domain.Tests;

public class DefaultPolicyContributorTests
{
    [Fact]
    public async Task Canonize_WithMinPolicy_ShouldRetainExistingLowerValue()
    {
        var persistence = new PolicyTestPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<PolicySampleCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Entity.RiskScore = 8;
                    return ValueTask.CompletedTask;
                });

                pipeline.AddStep(CanonPipelinePhase.Aggregation, (context, _) =>
                {
                    var existing = new PolicySampleCanon { RiskScore = 3 };
                    context.SetItem("canon:existing-entity", existing);
                    context.SetItem("canon:existing-metadata", existing.Metadata.Clone());
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var result = await runtime.Canonize(new PolicySampleCanon());

        result.Canonical.RiskScore.Should().Be(3);
        var footprint = result.Metadata.PropertyFootprints[nameof(PolicySampleCanon.RiskScore)];
        footprint.Policy.Should().Be(AggregationPolicyKind.Min.ToString());
        footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("existing");
    }

    [Fact]
    public async Task Canonize_WithMaxPolicy_ShouldSelectIncomingHigherValue()
    {
        var persistence = new PolicyTestPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<PolicySampleCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Entity.LifetimeSpend = 500m;
                    return ValueTask.CompletedTask;
                });

                pipeline.AddStep(CanonPipelinePhase.Aggregation, (context, _) =>
                {
                    var existing = new PolicySampleCanon { LifetimeSpend = 275m };
                    context.SetItem("canon:existing-entity", existing);
                    context.SetItem("canon:existing-metadata", existing.Metadata.Clone());
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var result = await runtime.Canonize(new PolicySampleCanon());

        result.Canonical.LifetimeSpend.Should().Be(500m);
        var footprint = result.Metadata.PropertyFootprints[nameof(PolicySampleCanon.LifetimeSpend)];
        footprint.Policy.Should().Be(AggregationPolicyKind.Max.ToString());
        footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("incoming");
    }

    [Fact]
    public async Task Canonize_WithLatestPolicyAndFutureExistingTimestamp_ShouldKeepExistingValue()
    {
        var persistence = new PolicyTestPersistence();
        var runtime = new CanonRuntimeBuilder()
            .UsePersistence(persistence)
            .ConfigurePipeline<PolicySampleCanon>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Entity.LifecycleStatus = "Incoming";
                    return ValueTask.CompletedTask;
                });

                pipeline.AddStep(CanonPipelinePhase.Aggregation, (context, _) =>
                {
                    var existing = new PolicySampleCanon { LifecycleStatus = "Committed" };
                    var existingMetadata = existing.Metadata.Clone();
                    existingMetadata.PropertyFootprints[nameof(PolicySampleCanon.LifecycleStatus)] = new CanonPropertyFootprint
                    {
                        Property = nameof(PolicySampleCanon.LifecycleStatus),
                        SourceKey = "legacy",
                        ArrivalToken = "existing-token",
                        ArrivedAt = DateTimeOffset.UtcNow.AddMinutes(5),
                        Value = "Committed",
                        Policy = AggregationPolicyKind.Latest.ToString()
                    };

                    context.SetItem("canon:existing-entity", existing);
                    context.SetItem("canon:existing-metadata", existingMetadata);
                    return ValueTask.CompletedTask;
                });
            })
            .Build();

        var options = CanonizationOptions.Default with { CorrelationId = "incoming-token" };
        var result = await runtime.Canonize(new PolicySampleCanon(), options);

        result.Canonical.LifecycleStatus.Should().Be("Committed");
        var footprint = result.Metadata.PropertyFootprints[nameof(PolicySampleCanon.LifecycleStatus)];
        footprint.Value.Should().Be("Committed");
        footprint.ArrivalToken.Should().Be("existing-token");
        footprint.Evidence.Should().ContainKey("winner").WhoseValue.Should().Be("existing");
    }

    private sealed class PolicySampleCanon : CanonEntity<PolicySampleCanon>
    {
        [AggregationKey]
        public string ExternalId { get; set; } = Guid.NewGuid().ToString("n");

        [AggregationPolicy(AggregationPolicyKind.Min)]
        public int RiskScore { get; set; }

        [AggregationPolicy(AggregationPolicyKind.Max)]
        public decimal LifetimeSpend { get; set; }

        [AggregationPolicy(AggregationPolicyKind.Latest)]
        public string? LifecycleStatus { get; set; }
    }

    private sealed class PolicyTestPersistence : ICanonPersistence
    {
        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(entity);

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(stage);

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult<CanonIndex?>(null);

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
