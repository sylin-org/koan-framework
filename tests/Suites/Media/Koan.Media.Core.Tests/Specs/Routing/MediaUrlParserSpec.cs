using Koan.Media.Web.Routing;

namespace Koan.Media.Core.Tests.Specs.Routing;

public sealed class MediaUrlParserSpec
{
    private static Dictionary<string, string> Q(params (string k, string v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v, StringComparer.OrdinalIgnoreCase);

    // ----- ad-hoc mode (no seed recipe) -----

    [Fact]
    public void AdHoc_with_no_params_produces_preserve_format_only_recipe()
    {
        var result = MediaUrlParser.Parse(seedRecipe: null, queryParams: Q());
        result.IgnoredParams.Should().BeEmpty();
        result.RejectedParams.Should().BeEmpty();
        var encode = result.Recipe.Steps.OfType<EncodeStep>().Single();
        encode.PreservesFormat.Should().BeTrue();
    }

    [Fact]
    public void AdHoc_width_only_creates_proportional_resize()
    {
        var result = MediaUrlParser.Parse(null, Q(("w", "600")));
        var resize = result.Recipe.Steps.OfType<ResizeStep>().Single();
        resize.Width.Should().Be(600);
        resize.Height.Should().BeNull();
    }

    [Fact]
    public void AdHoc_canonical_param_names_work()
    {
        var result = MediaUrlParser.Parse(null, Q(("width", "300"), ("height", "200")));
        var resize = result.Recipe.Steps.OfType<ResizeStep>().Single();
        resize.Width.Should().Be(300);
        resize.Height.Should().Be(200);
    }

    [Fact]
    public void AdHoc_aliases_resolve_to_canonical()
    {
        // w → width, h → height, q → quality, f → format
        var result = MediaUrlParser.Parse(null, Q(("w", "300"), ("h", "200"), ("f", "webp"), ("q", "75")));
        var resize = result.Recipe.Steps.OfType<ResizeStep>().Single();
        resize.Width.Should().Be(300);
        resize.Height.Should().Be(200);
        var encode = result.Recipe.Steps.OfType<EncodeStep>().Single();
        encode.Format.Should().Be("webp");
        encode.Quality.Should().Be(75);
    }

    [Fact]
    public void AdHoc_quality_preset_name_resolves()
    {
        var result = MediaUrlParser.Parse(null, Q(("format", "webp"), ("q", "web")));
        result.Recipe.Steps.OfType<EncodeStep>().Single().Quality.Should().Be(Quality.Web);
    }

    [Fact]
    public void AdHoc_crop_builds_shape_step()
    {
        var result = MediaUrlParser.Parse(null, Q(("crop", "square"), ("fit", "cover"), ("position", "top")));
        var shape = result.Recipe.Steps.OfType<ShapeStep>().Single();
        shape.Crop!.Value.Kind.Should().Be(CropSpecKind.Aspect);
        shape.Fit.Should().Be(Fit.Cover);
        shape.Position.Anchor.Should().Be(PositionAnchor.Top);
    }

    [Fact]
    public void AdHoc_position_without_crop_is_rejected()
    {
        var result = MediaUrlParser.Parse(null, Q(("position", "top")), strict: true);
        result.HasRejections.Should().BeTrue();
        result.RejectedParams.Should().Contain(s => s.StartsWith("position="));
    }

    [Fact]
    public void AdHoc_disabled_rejects_all_params()
    {
        var result = MediaUrlParser.Parse(null, Q(("w", "600")), adHocAllowed: false);
        result.HasRejections.Should().BeTrue();
        // Alias resolution normalises 'w' → 'width' before the rejection list is built
        result.RejectedParams.Should().Contain("width");
    }

    [Fact]
    public void AdHoc_unknown_param_is_ignored_when_not_strict()
    {
        var result = MediaUrlParser.Parse(null, Q(("zzz", "foo")), strict: false);
        result.HasRejections.Should().BeFalse();
        result.IgnoredParams.Should().Contain(s => s.StartsWith("zzz="));
    }

    [Fact]
    public void AdHoc_unknown_param_is_rejected_when_strict()
    {
        var result = MediaUrlParser.Parse(null, Q(("zzz", "foo")), strict: true);
        result.HasRejections.Should().BeTrue();
    }

    [Fact]
    public void AdHoc_invalid_int_for_dimension_is_rejected()
    {
        var result = MediaUrlParser.Parse(null, Q(("w", "notanumber")), strict: true);
        result.HasRejections.Should().BeTrue();
    }

    // ----- recipe-seed mode -----

    private static MediaRecipe SimpleResizeRecipe(MutatorKind mutators = MutatorKind.None) =>
        MediaRecipe.New()
            .WithName("thumb")
            .Resize(400, 300).Primary()
            .EncodeAs("webp", 80)
            .Mutators(mutators)
            .Build();

    [Fact]
    public void Recipe_seed_without_params_copies_steps()
    {
        var recipe = SimpleResizeRecipe();
        var result = MediaUrlParser.Parse(recipe, Q());
        result.Recipe.Steps.OfType<ResizeStep>().Single().Width.Should().Be(400);
        result.Recipe.Steps.OfType<EncodeStep>().Single().Format.Should().Be("webp");
    }

    [Fact]
    public void Width_override_rejected_when_dimensions_not_allowed()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.None);
        var result = MediaUrlParser.Parse(recipe, Q(("w", "200")), strict: true);
        result.HasRejections.Should().BeTrue();
        result.RejectedParams.Should().Contain(s => s.StartsWith("width="));
    }

    [Fact]
    public void Width_override_accepted_when_dimensions_allowed()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.Dimensions);
        var result = MediaUrlParser.Parse(recipe, Q(("w", "200")));
        result.HasRejections.Should().BeFalse();
        var resize = result.Recipe.Steps.OfType<ResizeStep>().Single();
        // Proportional scale: 400x300 → ?w=200 → 200x150
        resize.Width.Should().Be(200);
        resize.Height.Should().Be(150);
    }

    [Fact]
    public void Format_override_rejected_when_format_not_allowed()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.Dimensions); // no Format
        var result = MediaUrlParser.Parse(recipe, Q(("format", "jpeg")), strict: true);
        result.HasRejections.Should().BeTrue();
    }

    [Fact]
    public void Format_override_accepted_when_format_allowed()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.Format);
        var result = MediaUrlParser.Parse(recipe, Q(("format", "png")));
        result.Recipe.Steps.OfType<EncodeStep>().Single().Format.Should().Be("png");
    }

    [Fact]
    public void Quality_override_accepted_when_quality_allowed()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.Quality);
        var result = MediaUrlParser.Parse(recipe, Q(("q", "75")));
        result.Recipe.Steps.OfType<EncodeStep>().Single().Quality.Should().Be(75);
    }

    [Fact]
    public void Frame_override_rejected_when_no_frame_mutator()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.Common);
        var result = MediaUrlParser.Parse(recipe, Q(("frame", "2")), strict: true);
        result.HasRejections.Should().BeTrue();
    }

    [Fact]
    public void Frame_override_accepted_when_frame_mutator_present()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.Common | MutatorKind.Frame);
        var result = MediaUrlParser.Parse(recipe, Q(("frame", "2")));
        result.Recipe.Steps.OfType<ExtractFrameStep>().Single().Index.Should().Be(2);
    }

    [Fact]
    public void Crop_override_replaces_existing_shape_when_allowed()
    {
        var recipe = MediaRecipe.New()
            .WithName("hero")
            .Crop("16:9").Primary()
            .EncodeAs("webp")
            .Mutators(MutatorKind.Crop)
            .Build();
        var result = MediaUrlParser.Parse(recipe, Q(("crop", "1:1")));
        var shape = result.Recipe.Steps.OfType<ShapeStep>().Single();
        shape.Crop!.Value.Width.Should().Be(1);
        shape.Crop!.Value.Height.Should().Be(1);
    }

    [Fact]
    public void Named_step_addressing_targets_specific_step()
    {
        var recipe = MediaRecipe.New()
            .WithName("multi")
            .Resize(400, 300).Name("outer")
            .EncodeAs("webp")
            .Mutators(MutatorKind.Dimensions)
            .Build();
        var result = MediaUrlParser.Parse(recipe, Q(("outer.w", "800")));
        var resize = result.Recipe.Steps.OfType<ResizeStep>().Single();
        resize.Width.Should().Be(800);
    }

    [Fact]
    public void Named_step_addressing_unknown_name_is_rejected()
    {
        var recipe = SimpleResizeRecipe(MutatorKind.Dimensions);
        var result = MediaUrlParser.Parse(recipe, Q(("nonexistent.w", "100")), strict: true);
        result.HasRejections.Should().BeTrue();
    }
}
