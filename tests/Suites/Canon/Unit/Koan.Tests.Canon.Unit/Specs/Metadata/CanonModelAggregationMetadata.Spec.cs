namespace Koan.Tests.Canon.Unit.Specs.Metadata;

public sealed class CanonModelAggregationMetadataSpec
{
    private readonly ITestOutputHelper _output;

    public CanonModelAggregationMetadataSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Aggregation_keys_are_discovered_in_declaration_order()
        => TestPipeline.For<CanonModelAggregationMetadataSpec>(_output, nameof(Aggregation_keys_are_discovered_in_declaration_order))
            .Act(ctx =>
            {
                var metadata = CanonModelAggregationMetadata.For<ContactCanon>();

                metadata.ModelType.Should().Be(typeof(ContactCanon));
                metadata.AggregationKeyNames.Should().ContainInOrder("Email", "PhoneNumber");
                metadata.AuditEnabled.Should().BeTrue();

                metadata.PolicyByName.Should().ContainKey("Email");
                metadata.PolicyByName.Should().ContainKey("PhoneNumber");
                metadata.PolicyByName["Email"].Should().Be(AggregationPolicyKind.SourceOfTruth);
                metadata.PolicyByName["PhoneNumber"].Should().Be(AggregationPolicyKind.Max);

                var emailPolicy = metadata.GetRequiredPolicy("Email");
                emailPolicy.Kind.Should().Be(AggregationPolicyKind.SourceOfTruth);
                emailPolicy.HasAuthoritativeSources.Should().BeTrue();
                emailPolicy.AuthoritativeSources.Should().Contain(new[] { "crm", "erp" });
                emailPolicy.Fallback.Should().Be(AggregationPolicyKind.Latest);

                var phonePolicy = metadata.GetRequiredPolicy("PhoneNumber");
                phonePolicy.Kind.Should().Be(AggregationPolicyKind.Max);
                phonePolicy.HasAuthoritativeSources.Should().BeFalse();

                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Cache_returns_same_instance_for_same_type()
        => TestPipeline.For<CanonModelAggregationMetadataSpec>(_output, nameof(Cache_returns_same_instance_for_same_type))
            .Act(ctx =>
            {
                var first = CanonModelAggregationMetadata.For<ContactCanon>();
                var second = CanonModelAggregationMetadata.For<ContactCanon>();

                ReferenceEquals(first, second).Should().BeTrue();
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task Missing_aggregation_keys_throw_meaningful_exception()
        => TestPipeline.For<CanonModelAggregationMetadataSpec>(_output, nameof(Missing_aggregation_keys_throw_meaningful_exception))
            .Act(ctx =>
            {
                Action act = () => CanonModelAggregationMetadata.For<MissingKeyCanon>();
                act.Should().Throw<InvalidOperationException>()
                    .WithMessage("Canonical entity 'MissingKeyCanon' must declare at least one [AggregationKey] property.");
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Canon(audit: true)]
    private sealed class ContactCanon : CanonEntity<ContactCanon>
    {
        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "crm", Sources = new[] { "erp" })]
        public string Email { get; set; } = string.Empty;

        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.Max)]
        public string? PhoneNumber { get; set; }

        public string? DisplayName { get; set; }
    }

    private sealed class MissingKeyCanon : CanonEntity<MissingKeyCanon>
    {
        public string? DisplayName { get; set; }
    }
}
