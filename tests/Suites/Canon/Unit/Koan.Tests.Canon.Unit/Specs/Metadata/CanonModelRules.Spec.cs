namespace Koan.Tests.Canon.Unit.Specs.Metadata;

public sealed class CanonModelRulesSpec
{
    [Fact]
    public void Aggregation_keys_are_discovered_in_declaration_order()
    {
        var metadata = CanonModelRules.For<ContactCanon>();

        metadata.ModelType.Should().Be(typeof(ContactCanon));
        metadata.MatchKeyNames.Should().ContainInOrder("Email", "PhoneNumber");
        metadata.AuditEnabled.Should().BeTrue();

        metadata.PolicyByName.Should().ContainKey("Email");
        metadata.PolicyByName.Should().ContainKey("PhoneNumber");
        metadata.PolicyByName["Email"].Should().Be(Keep.From);
        metadata.PolicyByName["PhoneNumber"].Should().Be(Keep.Max);

        var emailPolicy = metadata.GetRequiredRule("Email");
        emailPolicy.Kind.Should().Be(Keep.From);
        emailPolicy.HasAuthoritativeSources.Should().BeTrue();
        emailPolicy.AuthoritativeSources.Should().Contain(new[] { "crm", "erp" });
        emailPolicy.Fallback.Should().Be(Keep.Latest); // reconcile rules always fall back to newest-wins

        var phonePolicy = metadata.GetRequiredRule("PhoneNumber");
        phonePolicy.Kind.Should().Be(Keep.Max);
        phonePolicy.HasAuthoritativeSources.Should().BeFalse();
    }

    [Fact]
    public void Cache_returns_same_instance_for_same_type()
    {
        var first = CanonModelRules.For<ContactCanon>();
        var second = CanonModelRules.For<ContactCanon>();

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void Missing_aggregation_keys_throw_meaningful_exception()
    {
        Action act = () => CanonModelRules.For<MissingKeyCanon>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Canonical entity 'MissingKeyCanon' must declare at least one [MatchKey] property.");
    }

    [Canon(audit: true)]
    private sealed class ContactCanon : CanonEntity<ContactCanon>
    {
        [MatchKey]
        [Reconcile(Keep.From, Source = "crm", Sources = new[] { "erp" })]
        public string Email { get; set; } = "";

        [MatchKey]
        [Reconcile(Keep.Max)]
        public string? PhoneNumber { get; set; }

        public string? DisplayName { get; set; }
    }

    private sealed class MissingKeyCanon : CanonEntity<MissingKeyCanon>
    {
        public string? DisplayName { get; set; }
    }
}
