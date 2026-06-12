namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class RecipeFingerprintSpec
{
    [Fact]
    public void Fingerprint_is_stable_across_construction_order()
    {
        // Same steps declared in two different orders should produce
        // the same fingerprint because the pipeline executes by stage.
        var a = MediaRecipe.New()
            .Resize(800, 600)
            .EncodeAs("webp", 80)
            .AutoOrient()
            .Build();
        var b = MediaRecipe.New()
            .AutoOrient()
            .EncodeAs("webp", 80)
            .Resize(800, 600)
            .Build();

        a.Fingerprint().Should().Be(b.Fingerprint());
    }

    [Fact]
    public void Fingerprint_differs_when_dimensions_change()
    {
        var a = MediaRecipe.New().Resize(800, 600).EncodeAs("webp", 80).Build();
        var b = MediaRecipe.New().Resize(801, 600).EncodeAs("webp", 80).Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }

    [Fact]
    public void Fingerprint_differs_when_format_changes()
    {
        var a = MediaRecipe.New().Resize(800, 600).EncodeAs("webp", 80).Build();
        var b = MediaRecipe.New().Resize(800, 600).EncodeAs("jpeg", 80).Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }

    [Fact]
    public void Fingerprint_differs_when_quality_changes()
    {
        var a = MediaRecipe.New().EncodeAs("webp", 80).Build();
        var b = MediaRecipe.New().EncodeAs("webp", 85).Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }

    [Fact]
    public void Fingerprint_differs_for_FlattenTo_vs_EncodeAs()
    {
        var a = MediaRecipe.New().EncodeAs("jpeg", 85).Build();
        var b = MediaRecipe.New().FlattenTo("jpeg", 85).Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint(),
            "FlattenTo and EncodeAs produce different bytes; fingerprints must diverge");
    }

    [Fact]
    public void Fingerprint_is_hex_of_8_bytes()
    {
        var f = MediaRecipe.New().Resize(100, 100).EncodeAs("webp", 80).Build().Fingerprint();
        f.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void Fingerprint_changes_with_version()
    {
        var a = MediaRecipe.New().Resize(100, 100).EncodeAs("webp", 80).WithVersion(1).Build();
        var b = MediaRecipe.New().Resize(100, 100).EncodeAs("webp", 80).WithVersion(2).Build();
        a.Fingerprint().Should().NotBe(b.Fingerprint());
    }
}
