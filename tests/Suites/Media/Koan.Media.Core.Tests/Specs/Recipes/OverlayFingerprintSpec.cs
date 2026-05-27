namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class OverlayFingerprintSpec
{
    [Fact]
    public void Same_overlay_config_produces_same_fingerprint()
    {
        var a = MediaRecipe.New()
            .Overlay("logo", size: OverlaySize.Fraction(0.1), position: Position.BottomRight)
            .EncodeAs("webp", 80).Build();
        var b = MediaRecipe.New()
            .Overlay("logo", size: OverlaySize.Fraction(0.1), position: Position.BottomRight)
            .EncodeAs("webp", 80).Build();
        a.Fingerprint().Should().Be(b.Fingerprint());
    }

    [Fact]
    public void Different_overlay_id_diverges()
    {
        var a = MediaRecipe.New().Overlay("logo-a").EncodeAs("webp").Build();
        var b = MediaRecipe.New().Overlay("logo-b").EncodeAs("webp").Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }

    [Fact]
    public void Different_overlay_recipe_diverges()
    {
        var a = MediaRecipe.New().Overlay("logo", recipeName: "mono-white").EncodeAs("webp").Build();
        var b = MediaRecipe.New().Overlay("logo", recipeName: "mono-black").EncodeAs("webp").Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }

    [Fact]
    public void Different_overlay_position_diverges()
    {
        var a = MediaRecipe.New().Overlay("logo", position: Position.BottomRight).EncodeAs("webp").Build();
        var b = MediaRecipe.New().Overlay("logo", position: Position.TopLeft).EncodeAs("webp").Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }

    [Fact]
    public void Layer_order_affects_fingerprint()
    {
        var a = MediaRecipe.New()
            .Overlay("logo")
            .Overlay("badge")
            .EncodeAs("webp").Build();
        var b = MediaRecipe.New()
            .Overlay("badge")
            .Overlay("logo")
            .EncodeAs("webp").Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint(),
            "z-order matters for composition output");
    }

    [Fact]
    public void Text_vs_media_source_diverges()
    {
        var a = MediaRecipe.New().Overlay("logo").EncodeAs("webp").Build();
        var b = MediaRecipe.New().OverlayText("logo").EncodeAs("webp").Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }
}
