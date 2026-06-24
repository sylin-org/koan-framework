using System.Collections.Generic;
using AwesomeAssertions;
using Koan.Core.Naming;
using Xunit;

namespace Koan.Tests.Core.Unit.Naming;

/// <summary>
/// ARCH-0096 / ARCH-0100: the single convergence point that folds the captured ambient axis bag into an identifier
/// through <see cref="IdentifierComposer"/> (instead of a per-pillar hand-rolled fold). Used by the Koan.Jobs
/// coalesce key and the storage blob key. Pins the no-op (null/empty), single + multi axis, deterministic ordering,
/// axis-disambiguation (equal values across axes don't collide), and position/separator selection.
/// </summary>
public class AmbientAxisComposerSpec
{
    [Fact]
    public void Null_bag_returns_the_anchor_unchanged()
        => AmbientAxisComposer.Append("base", null).Should().Be("base");

    [Fact]
    public void Empty_bag_returns_the_anchor_unchanged()
        => AmbientAxisComposer.Append("base", new Dictionary<string, string>()).Should().Be("base");

    [Fact]
    public void Single_axis_trails_the_anchor_with_axis_and_value()
        => AmbientAxisComposer.Append("base", new Dictionary<string, string> { ["koan:tenant"] = "acme" })
            .Should().Be("base|koan:tenant=acme");

    [Fact]
    public void Multiple_axes_order_deterministically_regardless_of_insertion_order()
    {
        var a = AmbientAxisComposer.Append("base", new Dictionary<string, string> { ["koan:tenant"] = "acme", ["koan:class"] = "phi" });
        var b = AmbientAxisComposer.Append("base", new Dictionary<string, string> { ["koan:class"] = "phi", ["koan:tenant"] = "acme" });
        a.Should().Be(b);   // axis-ordinal ordering → insertion order does not matter
    }

    [Fact]
    public void Two_tenants_get_distinct_identifiers()
    {
        var acme = AmbientAxisComposer.Append("k1", new Dictionary<string, string> { ["koan:tenant"] = "acme" });
        var globex = AmbientAxisComposer.Append("k1", new Dictionary<string, string> { ["koan:tenant"] = "globex" });
        acme.Should().NotBe(globex);   // the property the coalesce fold depends on
    }

    [Fact]
    public void Equal_values_on_different_axes_do_not_collide()
    {
        // The axis name is encoded in the token, so axis A=x and axis B=x compose distinctly.
        var ab = AmbientAxisComposer.Append("base", new Dictionary<string, string> { ["koan:a"] = "x", ["koan:b"] = "x" });
        var aOnly = AmbientAxisComposer.Append("base", new Dictionary<string, string> { ["koan:a"] = "x" });
        ab.Should().NotBe(aOnly);
        ab.Should().Contain("koan:a=x").And.Contain("koan:b=x");
    }

    [Fact]
    public void Leading_position_prefixes_the_anchor_for_a_blob_key_shape()
        => AmbientAxisComposer.Append("photo.jpg", new Dictionary<string, string> { ["koan:tenant"] = "acme" },
                ParticlePosition.Leading, "/")
            .Should().Be("koan:tenant=acme/photo.jpg");
}
