using Koan.Media.Web.Routing;

namespace Koan.Media.Core.Tests.Specs.Routing;

public sealed class OverlayUrlParserSpec
{
    private static Dictionary<string, string> Q(params (string k, string v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v, StringComparer.OrdinalIgnoreCase);

    // ----- Ad-hoc mode (no seed recipe) -----

    [Fact]
    public void AdHoc_bare_overlay_param_creates_layer_zero()
    {
        var r = MediaUrlParser.Parse(null, Q(("overlay", "logo-001")));
        var step = r.Recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers.Length.Should().Be(1);
        ((MediaOverlaySource)step.Layers[0].Source).MediaId.Should().Be("logo-001");
    }

    [Fact]
    public void AdHoc_overlay_with_position_and_size()
    {
        var r = MediaUrlParser.Parse(null, Q(
            ("overlay", "logo-001"),
            ("overlay.position", "br"),
            ("overlay.size", "0.1"),
            ("overlay.padding", "0.05"),
            ("overlay.opacity", "0.6")));

        var layer = r.Recipe.Steps.OfType<OverlayStep>().Single().Layers[0];
        layer.Position.Anchor.Should().Be(PositionAnchor.BottomRight);
        layer.Size.Kind.Should().Be(OverlaySizeKind.Fraction);
        layer.Size.FractionValue.Should().BeApproximately(0.1, 0.001);
        layer.Padding.IsFraction.Should().BeTrue();
        layer.Padding.FractionValue.Should().BeApproximately(0.05, 0.001);
        layer.Opacity.Should().BeApproximately(0.6, 0.001);
    }

    [Fact]
    public void AdHoc_indexed_overlays_route_to_distinct_layers()
    {
        var r = MediaUrlParser.Parse(null, Q(
            ("overlay.0.id", "logo"),
            ("overlay.0.position", "br"),
            ("overlay.1.id", "badge"),
            ("overlay.1.position", "tl")));

        var step = r.Recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers.Length.Should().Be(2);
        ((MediaOverlaySource)step.Layers[0].Source).MediaId.Should().Be("logo");
        step.Layers[0].Position.Anchor.Should().Be(PositionAnchor.BottomRight);
        ((MediaOverlaySource)step.Layers[1].Source).MediaId.Should().Be("badge");
        step.Layers[1].Position.Anchor.Should().Be(PositionAnchor.TopLeft);
    }

    [Fact]
    public void AdHoc_overlay_recipe_param_routes_to_source()
    {
        var r = MediaUrlParser.Parse(null, Q(
            ("overlay", "brand-logo"),
            ("overlay.recipe", "mono-white")));

        var src = (MediaOverlaySource)r.Recipe.Steps.OfType<OverlayStep>().Single().Layers[0].Source;
        src.RecipeName.Should().Be("mono-white");
    }

    [Fact]
    public void AdHoc_text_overlay_creates_text_source()
    {
        var r = MediaUrlParser.Parse(null, Q(
            ("overlay.text", "Hello"),
            ("overlay.font", "default"),
            ("overlay.color", "white"),
            ("overlay.fontsize", "48")));

        var src = (TextOverlaySource)r.Recipe.Steps.OfType<OverlayStep>().Single().Layers[0].Source;
        src.Text.Should().Be("Hello");
        src.Font.Should().Be("default");
        src.Color.Should().Be(BackgroundColor.White);
        src.FontSize.Should().Be(48);
    }

    [Fact]
    public void AdHoc_overlay_with_invalid_size_rejects_when_strict()
    {
        var r = MediaUrlParser.Parse(null,
            Q(("overlay", "logo"), ("overlay.size", "nonsense")),
            strict: true);
        r.HasRejections.Should().BeTrue();
    }

    [Fact]
    public void AdHoc_overlay_with_neither_id_nor_text_skips_layer()
    {
        // Only "overlay.position" is set — no source means no layer is created.
        var r = MediaUrlParser.Parse(null, Q(("overlay.position", "br")));
        r.Recipe.Steps.OfType<OverlayStep>().Any().Should().BeFalse();
    }

    // ----- Recipe-seed mode -----

    private static MediaRecipe RecipeWithoutOverlayMutator() => MediaRecipe.New()
        .WithName("hero")
        .Resize(800, 600).Primary()
        .EncodeAs("webp", 80)
        .Mutators(MutatorKind.Common)  // no Overlay
        .Build();

    private static MediaRecipe RecipeWithOverlayMutator() => MediaRecipe.New()
        .WithName("hero-overlay")
        .Resize(800, 600).Primary()
        .EncodeAs("webp", 80)
        .Mutators(MutatorKind.Common | MutatorKind.Overlay)
        .Build();

    [Fact]
    public void Recipe_seed_with_overlay_params_rejects_without_mutator()
    {
        var recipe = RecipeWithoutOverlayMutator();
        var r = MediaUrlParser.Parse(recipe, Q(("overlay", "logo")), strict: true);
        r.HasRejections.Should().BeTrue();
        r.RejectedParams.Any(p => p.StartsWith("overlay.")).Should().BeTrue();
    }

    [Fact]
    public void Recipe_seed_with_overlay_mutator_allowed_appends_layer()
    {
        var recipe = RecipeWithOverlayMutator();
        var r = MediaUrlParser.Parse(recipe, Q(
            ("overlay", "logo"),
            ("overlay.position", "br")));

        r.HasRejections.Should().BeFalse();
        var step = r.Recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers.Length.Should().Be(1);
        ((MediaOverlaySource)step.Layers[0].Source).MediaId.Should().Be("logo");
    }

    [Fact]
    public void Recipe_with_existing_overlay_layers_seeds_them()
    {
        var seedRecipe = MediaRecipe.New()
            .WithName("watermarked")
            .Resize(800, 600)
            .Overlay("brand-logo", position: Position.BottomRight)
            .EncodeAs("webp", 80)
            .Mutators(MutatorKind.None)
            .Build();

        var r = MediaUrlParser.Parse(seedRecipe, Q());
        var step = r.Recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers.Length.Should().Be(1);
        ((MediaOverlaySource)step.Layers[0].Source).MediaId.Should().Be("brand-logo");
    }

    [Fact]
    public void Recipe_with_existing_overlay_plus_new_layers_appends_when_allowed()
    {
        var seedRecipe = MediaRecipe.New()
            .WithName("watermarked")
            .Overlay("brand-logo", position: Position.BottomRight)
            .EncodeAs("webp", 80)
            .Mutators(MutatorKind.Overlay)
            .Build();

        var r = MediaUrlParser.Parse(seedRecipe, Q(
            ("overlay.0.id", "additional"),
            ("overlay.0.position", "tl")));

        // The recipe's original brand-logo layer is copied in, then the URL's
        // overlay.0 layer is appended after. Two layers total, brand-logo first.
        var step = r.Recipe.Steps.OfType<OverlayStep>().Single();
        step.Layers.Length.Should().Be(2);
        ((MediaOverlaySource)step.Layers[0].Source).MediaId.Should().Be("brand-logo");
        ((MediaOverlaySource)step.Layers[1].Source).MediaId.Should().Be("additional");
    }
}
