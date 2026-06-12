using AwesomeAssertions;
using Koan.Core.Capabilities;
using Xunit;

namespace Koan.Core.Tests;

/// <summary>Conformance specs for the unified capability primitive (ARCH-0084).</summary>
public class CapabilitySetTests
{
    private static readonly Capability A = new("test.a");
    private static readonly Capability B = new("test.b");

    [Fact]
    public void Has_is_true_only_for_declared_tokens()
    {
        var caps = CapabilitySet.Build("test.owner", c => c.Add(A));
        caps.Has(A).Should().BeTrue();
        caps.Has(B).Should().BeFalse();
    }

    [Fact]
    public void Require_throws_naming_owner_and_token_when_absent()
    {
        var caps = CapabilitySet.Build("data.postgres", c => c.Add(A));
        var act = () => caps.Require(B);
        act.Should().Throw<CapabilityNotSupportedException>()
            .Which.Message.Should().Contain("data.postgres").And.Contain("test.b");
    }

    [Fact]
    public void Require_does_not_throw_when_present()
    {
        var caps = CapabilitySet.Build(null, c => c.Add(A));
        var act = () => caps.Require(A);
        act.Should().NotThrow();
    }

    [Fact]
    public void Detail_returns_attached_value_and_default_otherwise()
    {
        var caps = CapabilitySet.Build(null, c => c.Add(A, "payload").Add(B));
        caps.Detail<string>(A).Should().Be("payload");
        caps.Detail<string>(B).Should().BeNull();                       // token present, no detail
        caps.Detail<string>(new Capability("absent")).Should().BeNull();
        caps.Detail<int?>(A).Should().BeNull();                         // wrong detail type -> default
    }

    [Fact]
    public void All_reports_every_declared_token()
    {
        var caps = CapabilitySet.Build(null, c => c.Add(A).Add(B));
        caps.All.Should().BeEquivalentTo(new[] { A, B });
    }

    [Fact]
    public void Capability_equality_is_by_id()
    {
        new Capability("x").Should().Be(new Capability("x"));
        new Capability("x").Should().NotBe(new Capability("y"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Capability_rejects_blank_id(string id)
    {
        var act = () => new Capability(id);
        act.Should().Throw<ArgumentException>();
    }
}
