namespace Koan.Tests.Canon.Unit.Specs.Metadata;

public sealed class CanonModelAggregationMetadataSpec
{
    [Fact]
    public void Aggregation_keys_are_discovered_in_declaration_order()
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
    }

    [Fact]
    public void Cache_returns_same_instance_for_same_type()
    {
        var first = CanonModelAggregationMetadata.For<ContactCanon>();
        var second = CanonModelAggregationMetadata.For<ContactCanon>();

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void Missing_aggregation_keys_throw_meaningful_exception()
    {
        Action act = () => CanonModelAggregationMetadata.For<MissingKeyCanon>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Canonical entity 'MissingKeyCanon' must declare at least one [AggregationKey] property.");
    }

    [Canon(audit: true)]
    private sealed class ContactCanon : CanonEntity<ContactCanon>
    {
        [AggregationKey]
        [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "crm", Sources = new[] { "erp" })]
        public string Email { get; set; } = "";

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
