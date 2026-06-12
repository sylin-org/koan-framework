using System.Text.Json;
using System.Text.Json.Nodes;

namespace Koan.Media.Core.Tests.Specs.Registry;

public sealed class OverlayJsonRoundTripSpec
{
    [Fact]
    public void Serialize_emits_overlay_with_layer_array()
    {
        var recipe = MediaRecipe.New()
            .Overlay("logo", size: OverlaySize.Fraction(0.1), position: Position.BottomRight,
                padding: OverlayPadding.FromFraction(0.05), opacity: 0.6, rotate: 15,
                recipeName: "mono-white")
            .EncodeAs("webp", 80)
            .Build();

        var json = RecipeJsonSerializer.Serialize(recipe);
        var steps = json["steps"]!.AsArray();
        var overlayStep = steps.Single(s => s!["op"]!.GetValue<string>() == "overlay");
        var layers = overlayStep!["layers"]!.AsArray();
        layers.Should().HaveCount(1);

        var layer = layers[0]!.AsObject();
        layer["source"]!["kind"]!.GetValue<string>().Should().Be("media");
        layer["source"]!["mediaId"]!.GetValue<string>().Should().Be("logo");
        layer["source"]!["recipe"]!.GetValue<string>().Should().Be("mono-white");
        layer["size"]!.GetValue<string>().Should().Be("0.1");
        layer["position"]!.GetValue<string>().Should().Be("bottom-right");
        layer["padding"]!.GetValue<string>().Should().Be("0.05");
        layer["opacity"]!.GetValue<double>().Should().BeApproximately(0.6, 0.001);
        layer["rotate"]!.GetValue<int>().Should().Be(15);
    }

    [Fact]
    public void Serialize_text_layer()
    {
        var recipe = MediaRecipe.New()
            .OverlayText("Hello", font: "default", color: BackgroundColor.White, fontSize: 24)
            .EncodeAs("webp")
            .Build();

        var layer = RecipeJsonSerializer.Serialize(recipe)["steps"]!.AsArray()
            .Single(s => s!["op"]!.GetValue<string>() == "overlay")!["layers"]!.AsArray()[0]!.AsObject();

        layer["source"]!["kind"]!.GetValue<string>().Should().Be("text");
        layer["source"]!["text"]!.GetValue<string>().Should().Be("Hello");
        layer["source"]!["color"]!.GetValue<string>().Should().Be("ffffff");
        layer["source"]!["fontSize"]!.GetValue<int>().Should().Be(24);
    }

    [Fact]
    public void Round_trip_via_appsettings_form_preserves_overlay_layers()
    {
        var original = MediaRecipe.New()
            .WithName("watermarked")
            .Overlay("logo", size: OverlaySize.Fraction(0.1), position: Position.BottomRight,
                padding: OverlayPadding.FromPixels(20), recipeName: "mono-white")
            .Overlay("badge", position: Position.TopLeft)
            .EncodeAs("webp", 82)
            .Build();

        // Serialise as appsettings, deserialise into ConfiguredRecipe, rebuild
        var wrapped = RecipeJsonSerializer.SerializeAsAppSettings(original);
        var inner = wrapped["Koan"]!["Media"]!["Recipes"]!["watermarked"]!.AsObject();
        var configured = JsonSerializer.Deserialize<ConfiguredRecipe>(
            inner.ToJsonString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        configured.Should().NotBeNull();
        var rebuilt = ConfiguredRecipeBinder.Bind("watermarked", configured!, RecipeSource.Config);
        rebuilt.Fingerprint().Should().Be(original.Fingerprint(),
            "appsettings round-trip must produce an identical overlay step");
    }

    [Fact]
    public void Multi_layer_emits_in_declared_order()
    {
        var recipe = MediaRecipe.New()
            .Overlay("first")
            .Overlay("second")
            .Overlay("third")
            .EncodeAs("webp").Build();

        var layers = RecipeJsonSerializer.Serialize(recipe)["steps"]!.AsArray()
            .Single(s => s!["op"]!.GetValue<string>() == "overlay")!["layers"]!.AsArray();
        layers.Should().HaveCount(3);
        layers[0]!["source"]!["mediaId"]!.GetValue<string>().Should().Be("first");
        layers[1]!["source"]!["mediaId"]!.GetValue<string>().Should().Be("second");
        layers[2]!["source"]!["mediaId"]!.GetValue<string>().Should().Be("third");
    }
}
