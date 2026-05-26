namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class PositionParserSpec
{
    [Theory]
    [InlineData("center", PositionAnchor.Center)]
    [InlineData("top", PositionAnchor.Top)]
    [InlineData("bottom", PositionAnchor.Bottom)]
    [InlineData("left", PositionAnchor.Left)]
    [InlineData("right", PositionAnchor.Right)]
    [InlineData("top-left", PositionAnchor.TopLeft)]
    [InlineData("tr", PositionAnchor.TopRight)]
    [InlineData("BL", PositionAnchor.BottomLeft)]
    [InlineData("bottomright", PositionAnchor.BottomRight)]
    public void Parses_named_anchors(string input, PositionAnchor expected)
    {
        Position.TryParse(input, out var p).Should().BeTrue();
        p.Anchor.Should().Be(expected);
    }

    [Fact]
    public void Focus_sentinel()
    {
        Position.TryParse("focus", out var p).Should().BeTrue();
        p.UseFocus.Should().BeTrue();
    }

    [Theory]
    [InlineData("33%", 0.33, 0.5)]
    [InlineData("0%", 0.0, 0.5)]
    [InlineData("100%", 1.0, 0.5)]
    public void Single_percent_stores_on_x_axis(string input, double x, double y)
    {
        Position.TryParse(input, out var p).Should().BeTrue();
        p.Anchor.Should().BeNull();
        p.X.Should().BeApproximately(x, 0.01);
        p.Y.Should().BeApproximately(y, 0.01);
    }

    [Theory]
    [InlineData("x:33,y:50", 0.33, 0.50)]
    [InlineData("x:100,y:0", 1.0, 0.0)]
    [InlineData("x:0,y:100", 0.0, 1.0)]
    public void Per_axis_percent(string input, double x, double y)
    {
        Position.TryParse(input, out var p).Should().BeTrue();
        p.Anchor.Should().BeNull();
        p.X.Should().BeApproximately(x, 0.01);
        p.Y.Should().BeApproximately(y, 0.01);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("x:abc,y:50")]
    [InlineData("x:50")]
    [InlineData("x:50,y:abc")]
    public void Rejects_invalid(string? input)
    {
        Position.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Canonical_form_for_named_anchor()
    {
        Position.Center.ToCanonical().Should().Be("center");
        Position.TopLeft.ToCanonical().Should().Be("top-left");
        Position.Focus.ToCanonical().Should().Be("focus");
    }

    [Fact]
    public void Canonical_form_for_percent()
    {
        var p = Position.Percent(0.33, 0.5);
        p.ToCanonical().Should().Be("x:33,y:50");
    }

    [Fact]
    public void Clamps_out_of_range_percents()
    {
        var over = Position.Percent(1.5, -0.3);
        over.X.Should().Be(1.0);
        over.Y.Should().Be(0.0);
    }
}
