namespace Koan.Media.Core.Tests.Specs.Registry;

public sealed class ConfiguredRecipeBinderSpec
{
    [Fact]
    public void Binds_simple_resize_plus_encode()
    {
        var configured = new ConfiguredRecipe
        {
            Description = "thumb",
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "resize", Width = 400 },
                new() { Op = "encodeAs", Format = "webp", Quality = 80 },
            },
        };
        var recipe = ConfiguredRecipeBinder.Bind("thumb", configured, RecipeSource.Config);
        recipe.Name.Should().Be("thumb");
        recipe.Description.Should().Be("thumb");
        recipe.Steps.OfType<ResizeStep>().Single().Width.Should().Be(400);
        recipe.Steps.OfType<EncodeStep>().Single().Format.Should().Be("webp");
    }

    [Fact]
    public void Binds_shape_step_with_full_vocabulary()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "shape", Crop = "16:9", Mode = "contain", Position = "top", Bg = "black" },
                new() { Op = "encodeAs", Format = "jpeg", Quality = 85 },
            },
        };
        var recipe = ConfiguredRecipeBinder.Bind("hero", configured, RecipeSource.Config);
        var shape = recipe.Steps.OfType<ShapeStep>().Single();
        shape.Crop.Should().NotBeNull();
        shape.Fit.Should().Be(Fit.Contain);
        shape.Position.Anchor.Should().Be(PositionAnchor.Top);
        shape.Background.Kind.Should().Be(BackgroundKind.Solid);
    }

    [Fact]
    public void Binds_aspect_as_crop_alias()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "shape", Aspect = "square" },
                new() { Op = "encodeAs", Format = "webp" },
            },
        };
        var recipe = ConfiguredRecipeBinder.Bind("sq", configured, RecipeSource.Config);
        var shape = recipe.Steps.OfType<ShapeStep>().Single();
        shape.Crop.Should().NotBeNull();
        shape.Crop!.Value.Kind.Should().Be(CropSpecKind.Aspect);
    }

    [Fact]
    public void Binds_extract_frame_and_flatten_to()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "extractFrame", Index = 0 },
                new() { Op = "flattenTo", Format = "jpeg", Quality = 85 },
            },
        };
        var recipe = ConfiguredRecipeBinder.Bind("poster", configured, RecipeSource.Config);
        recipe.Steps.OfType<ExtractFrameStep>().Single().Index.Should().Be(0);
        recipe.Steps.OfType<FlattenToStep>().Single().Format.Should().Be("jpeg");
    }

    [Fact]
    public void Binds_strip_kinds_string()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "strip", Kinds = "exif,xmp" },
                new() { Op = "encodeAs", Format = "webp" },
            },
        };
        var recipe = ConfiguredRecipeBinder.Bind("clean", configured, RecipeSource.Config);
        var strip = recipe.Steps.OfType<StripStep>().Single();
        strip.Kinds.Should().Be(MetadataKinds.Exif | MetadataKinds.Xmp);
    }

    [Fact]
    public void Encode_with_null_format_means_preserve_source()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "encodeAs", Format = null },
            },
        };
        var recipe = ConfiguredRecipeBinder.Bind("passthrough", configured, RecipeSource.Config);
        recipe.Steps.OfType<EncodeStep>().Single().PreservesFormat.Should().BeTrue();
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("orient_typo")]
    public void Unknown_op_throws_with_offending_op_name(string badOp)
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep> { new() { Op = badOp } },
        };
        var act = () => ConfiguredRecipeBinder.Bind("test", configured, RecipeSource.Config);
        act.Should().Throw<MediaRecipeBindingException>().WithMessage($"*{badOp}*");
    }

    [Fact]
    public void Invalid_crop_value_throws()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "crop", Crop = "nonsense" },
            },
        };
        var act = () => ConfiguredRecipeBinder.Bind("test", configured, RecipeSource.Config);
        act.Should().Throw<MediaRecipeBindingException>().WithMessage("*Invalid crop*");
    }

    [Fact]
    public void Mutators_are_aggregated()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep> { new() { Op = "encodeAs" } },
            Mutators = new List<string> { "dimensions", "format", "quality" },
        };
        var recipe = ConfiguredRecipeBinder.Bind("t", configured, RecipeSource.Config);
        recipe.AllowedMutators.Should().Be(MutatorKind.Dimensions | MutatorKind.Format | MutatorKind.Quality);
    }

    [Fact]
    public void Name_and_Primary_on_steps_are_preserved()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "resize", Width = 800, Name = "size", Primary = true },
                new() { Op = "encodeAs", Format = "webp" },
            },
        };
        var recipe = ConfiguredRecipeBinder.Bind("t", configured, RecipeSource.Config);
        var resize = recipe.Steps.OfType<ResizeStep>().Single();
        resize.Name.Should().Be("size");
        resize.Primary.Should().BeTrue();
    }
}
