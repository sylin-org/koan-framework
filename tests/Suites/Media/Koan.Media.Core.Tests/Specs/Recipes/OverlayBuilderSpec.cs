namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class OverlayBuilderSpec
{
    [Fact]
    public void Single_overlay_creates_overlay_step_with_one_layer()
    {
        var recipe = MediaRecipe.New()
            .Overlay("logo-001", size: OverlaySize.Fraction(0.1), position: Position.BottomRight)
            .Build();

        var step = recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers.Length.Should().Be(1);
        var src = (MediaOverlaySource)step.Layers[0].Source;
        src.MediaId.Should().Be("logo-001");
        step.Layers[0].Size.Kind.Should().Be(OverlaySizeKind.Fraction);
        step.Layers[0].Position.Anchor.Should().Be(PositionAnchor.BottomRight);
    }

    [Fact]
    public void Two_overlays_append_layers_to_same_step()
    {
        var recipe = MediaRecipe.New()
            .Overlay("logo", position: Position.BottomRight)
            .Overlay("badge", position: Position.TopLeft)
            .Build();

        var step = recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers.Length.Should().Be(2);
        ((MediaOverlaySource)step.Layers[0].Source).MediaId.Should().Be("logo");
        ((MediaOverlaySource)step.Layers[1].Source).MediaId.Should().Be("badge");
    }

    [Fact]
    public void Overlay_text_creates_text_source_layer()
    {
        var recipe = MediaRecipe.New()
            .OverlayText("Hello", font: "default", color: BackgroundColor.White, fontSize: 48)
            .Build();

        var step = recipe.Steps.OfType<OverlayStep>().Single();
        var src = (TextOverlaySource)step.Layers[0].Source;
        src.Text.Should().Be("Hello");
        src.Font.Should().Be("default");
        src.FontSize.Should().Be(48);
        src.Color.Should().Be(BackgroundColor.White);
    }

    [Fact]
    public void Empty_overlay_id_throws()
    {
        var act = () => MediaRecipe.New().Overlay("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Empty_overlay_text_throws()
    {
        var act = () => MediaRecipe.New().OverlayText("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Opacity_clamps_to_0_1_range()
    {
        var recipe = MediaRecipe.New()
            .Overlay("logo", opacity: 1.5)
            .Build();
        var step = recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers[0].Opacity.Should().Be(1.0);

        var recipe2 = MediaRecipe.New()
            .Overlay("logo", opacity: -0.5)
            .Build();
        var step2 = recipe2.Steps.OfType<OverlayStep>().Single();
        step2.Layers[0].Opacity.Should().Be(0.0);
    }

    [Fact]
    public void Recipe_name_on_overlay_round_trips_to_source()
    {
        var recipe = MediaRecipe.New()
            .Overlay("brand", recipeName: "mono-white")
            .Build();
        var step = recipe.Steps.OfType<OverlayStep>().Single();
        var src = (MediaOverlaySource)step.Layers[0].Source;
        src.RecipeName.Should().Be("mono-white");
    }
}
