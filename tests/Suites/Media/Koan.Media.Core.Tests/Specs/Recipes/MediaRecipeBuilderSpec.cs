namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class MediaRecipeBuilderSpec
{
    [Fact]
    public void Empty_builder_produces_implicit_format_preserving_encode()
    {
        var recipe = MediaRecipe.New().Build();
        var encode = recipe.Steps.OfType<EncodeStep>().Single();
        encode.PreservesFormat.Should().BeTrue();
        encode.Format.Should().BeNull();
    }

    [Fact]
    public void Explicit_encode_replaces_implicit()
    {
        var recipe = MediaRecipe.New().EncodeAs("webp", 80).Build();
        recipe.Steps.OfType<EncodeStep>().Should().ContainSingle()
            .Which.Format.Should().Be("webp");
    }

    [Fact]
    public void FlattenTo_replaces_encode_slot()
    {
        var recipe = MediaRecipe.New().EncodeAs("png").FlattenTo("jpeg", 85).Build();
        // FlattenTo is in the Encode stage; replaces the EncodeStep
        recipe.Steps.Should().NotContain(s => s is EncodeStep);
        recipe.Steps.OfType<FlattenToStep>().Should().ContainSingle()
            .Which.Format.Should().Be("jpeg");
    }

    [Fact]
    public void Shape_is_single_slot()
    {
        var recipe = MediaRecipe.New()
            .Crop("16:9")
            .Crop("square")  // replaces
            .Build();
        recipe.Steps.OfType<ShapeStep>().Should().ContainSingle()
            .Which.Crop.Should().Be(CropSpec.Square);
    }

    [Fact]
    public void Size_is_single_slot()
    {
        var recipe = MediaRecipe.New()
            .Resize(100, 100)
            .Resize(200, 200) // replaces
            .Build();
        var resize = recipe.Steps.OfType<ResizeStep>().Single();
        resize.Width.Should().Be(200);
        resize.Height.Should().Be(200);
    }

    [Fact]
    public void Name_and_Primary_apply_to_last_step()
    {
        var recipe = MediaRecipe.New()
            .Resize(800, 600).Name("size").Primary()
            .EncodeAs("webp", 80)
            .Build();
        var resize = recipe.Steps.OfType<ResizeStep>().Single();
        resize.Name.Should().Be("size");
        resize.Primary.Should().BeTrue();
        var encode = recipe.Steps.OfType<EncodeStep>().Single();
        encode.Name.Should().BeNull();
        encode.Primary.Should().BeFalse();
    }

    [Fact]
    public void Name_without_preceding_step_throws()
    {
        var act = () => MediaRecipe.New().Name("orphan").Build();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Primary_without_preceding_step_throws()
    {
        var act = () => MediaRecipe.New().Primary().Build();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Mutators_are_recorded()
    {
        var recipe = MediaRecipe.New()
            .Mutators(MutatorKind.Common | MutatorKind.Frame)
            .Build();
        recipe.AllowedMutators.Should().Be(MutatorKind.Common | MutatorKind.Frame);
    }

    [Fact]
    public void Implicit_conversion_to_recipe()
    {
        MediaRecipe recipe = MediaRecipe.New().EncodeAs("webp", 80);
        recipe.Steps.OfType<EncodeStep>().Single().Format.Should().Be("webp");
    }

    [Fact]
    public void Crop_with_invalid_string_throws()
    {
        var act = () => MediaRecipe.New().Crop("garbage").Build();
        act.Should().Throw<ArgumentException>();
    }
}
