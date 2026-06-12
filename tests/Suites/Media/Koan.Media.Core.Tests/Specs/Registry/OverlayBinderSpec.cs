namespace Koan.Media.Core.Tests.Specs.Registry;

public sealed class OverlayBinderSpec
{
    [Fact]
    public void Binds_single_media_overlay_layer()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new()
                {
                    Op = "overlay",
                    Layers = new List<ConfiguredOverlayLayer>
                    {
                        new()
                        {
                            Source = new ConfiguredOverlaySource { Kind = "media", MediaId = "logo-001", Recipe = "mono-white" },
                            Size = "0.1",
                            Position = "br",
                            Padding = "0.05",
                            Opacity = 0.8,
                        },
                    },
                },
                new() { Op = "encodeAs", Format = "webp", Quality = 80 },
            },
        };

        var recipe = ConfiguredRecipeBinder.Bind("watermarked", configured, RecipeSource.Config);
        var step = recipe.Steps.OfType<OverlayStep>().Single();
        var src = (MediaOverlaySource)step.Layers[0].Source;
        src.MediaId.Should().Be("logo-001");
        src.RecipeName.Should().Be("mono-white");
        step.Layers[0].Size.Kind.Should().Be(OverlaySizeKind.Fraction);
        step.Layers[0].Position.Anchor.Should().Be(PositionAnchor.BottomRight);
        step.Layers[0].Padding.IsFraction.Should().BeTrue();
        step.Layers[0].Opacity.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void Binds_text_overlay_layer_with_color_and_font_size()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new()
                {
                    Op = "overlay",
                    Layers = new List<ConfiguredOverlayLayer>
                    {
                        new()
                        {
                            Source = new ConfiguredOverlaySource
                            {
                                Kind = "text",
                                Text = "© {{year}}",
                                Font = "default",
                                Color = "white",
                                FontSize = 48,
                            },
                            Position = "bottom",
                        },
                    },
                },
                new() { Op = "encodeAs", Format = "jpeg" },
            },
        };

        var recipe = ConfiguredRecipeBinder.Bind("stamped", configured, RecipeSource.Config);
        var src = (TextOverlaySource)recipe.Steps.OfType<OverlayStep>().Single().Layers[0].Source;
        src.Text.Should().Be("© {{year}}");
        src.Font.Should().Be("default");
        src.Color.Should().Be(BackgroundColor.White);
        src.FontSize.Should().Be(48);
    }

    [Fact]
    public void Binds_multi_layer_overlays()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new()
                {
                    Op = "overlay",
                    Layers = new List<ConfiguredOverlayLayer>
                    {
                        new() { Source = new ConfiguredOverlaySource { MediaId = "logo" } },
                        new() { Source = new ConfiguredOverlaySource { MediaId = "badge" } },
                    },
                },
                new() { Op = "encodeAs", Format = "webp" },
            },
        };

        var recipe = ConfiguredRecipeBinder.Bind("two-layer", configured, RecipeSource.Config);
        recipe.Steps.OfType<OverlayStep>().Single().Layers.Length.Should().Be(2);
    }

    [Fact]
    public void Empty_layers_throws()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new() { Op = "overlay", Layers = new List<ConfiguredOverlayLayer>() },
            },
        };
        var act = () => ConfiguredRecipeBinder.Bind("t", configured, RecipeSource.Config);
        act.Should().Throw<MediaRecipeBindingException>();
    }

    [Fact]
    public void Media_source_without_id_throws()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new()
                {
                    Op = "overlay",
                    Layers = new List<ConfiguredOverlayLayer>
                    {
                        new() { Source = new ConfiguredOverlaySource { Kind = "media", MediaId = "" } },
                    },
                },
            },
        };
        var act = () => ConfiguredRecipeBinder.Bind("t", configured, RecipeSource.Config);
        act.Should().Throw<MediaRecipeBindingException>().WithMessage("*media*requires*mediaId*");
    }

    [Fact]
    public void Unknown_source_kind_throws()
    {
        var configured = new ConfiguredRecipe
        {
            Steps = new List<ConfiguredStep>
            {
                new()
                {
                    Op = "overlay",
                    Layers = new List<ConfiguredOverlayLayer>
                    {
                        new() { Source = new ConfiguredOverlaySource { Kind = "telepathy" } },
                    },
                },
            },
        };
        var act = () => ConfiguredRecipeBinder.Bind("t", configured, RecipeSource.Config);
        act.Should().Throw<MediaRecipeBindingException>().WithMessage("*Unknown overlay source kind*");
    }
}
