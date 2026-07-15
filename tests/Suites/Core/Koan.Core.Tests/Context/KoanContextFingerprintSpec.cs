using AwesomeAssertions;
using Koan.Core.Context;
using Xunit;

namespace Koan.Core.Tests.Context;

public sealed class KoanContextFingerprintSpec
{
    [Fact]
    public void Fingerprint_is_deterministic_and_independent_of_bag_enumeration_order()
    {
        var first = new Dictionary<string, string>
        {
            ["koan:tenant"] = "v1:id:acme",
            ["koan:subject"] = "v1:id:operator"
        };
        var second = new Dictionary<string, string>
        {
            ["koan:subject"] = "v1:id:operator",
            ["koan:tenant"] = "v1:id:acme"
        };

        KoanContextFingerprint.Compute(first, "entity", "42")
            .Should().Be(KoanContextFingerprint.Compute(second, "entity", "42"));
    }

    [Fact]
    public void Fingerprint_changes_with_context_or_logical_identity()
    {
        var tenantA = new Dictionary<string, string> { ["koan:tenant"] = "v1:id:a" };
        var tenantB = new Dictionary<string, string> { ["koan:tenant"] = "v1:id:b" };
        var baseline = KoanContextFingerprint.Compute(tenantA, "entity", "42");

        baseline.Should().NotBe(KoanContextFingerprint.Compute(tenantB, "entity", "42"));
        baseline.Should().NotBe(KoanContextFingerprint.Compute(tenantA, "entity", "43"));
        baseline.Should().NotBe(KoanContextFingerprint.Compute(tenantA, "ent", "ity42"));
    }

    [Fact]
    public void Fingerprint_is_fixed_width_and_does_not_echo_context_values()
    {
        const string privateValue = "v1:id:private-tenant";
        var fingerprint = KoanContextFingerprint.Compute(
            new Dictionary<string, string> { ["koan:tenant"] = privateValue },
            "entity",
            "42");

        fingerprint.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        fingerprint.Should().NotContain(privateValue).And.NotContain("private-tenant");
    }

    [Fact]
    public void Length_delimiting_prevents_identity_part_boundary_collisions()
        => KoanContextFingerprint.Compute(null, "ab", "c")
            .Should().NotBe(KoanContextFingerprint.Compute(null, "a", "bc"));
}
