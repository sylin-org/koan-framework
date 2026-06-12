using System.Text.Json.Nodes;

namespace Koan.Media.Core.Tests.Specs.Registry;

public sealed class RecipeJsonSerializerSpec
{
    [Fact]
    public void Serializes_basic_recipe_with_all_canonical_fields()
    {
        var recipe = MediaRecipe.New()
            .WithName("poster")
            .WithDescription("test poster")
            .Sample(new FrameSelector.Index(0))
            .Crop("1:1")
            .Resize(800).Name("size").Primary()
            .EncodeAs("webp", 80)
            .Mutators(MutatorKind.Common | MutatorKind.Frame)
            .Build();

        var json = RecipeJsonSerializer.Serialize(recipe);
        json["name"]!.GetValue<string>().Should().Be("poster");
        json["description"]!.GetValue<string>().Should().Be("test poster");
        json["fingerprint"]!.GetValue<string>().Should().Be(recipe.Fingerprint());
        json["steps"]!.AsArray().Should().HaveCount(4);
    }

    [Fact]
    public void Serializes_steps_in_canonical_stage_order()
    {
        var recipe = MediaRecipe.New()
            .EncodeAs("webp")          // declared first but encode = last stage
            .Resize(400, 300)          // size stage
            .Sample(new FrameSelector.Index(0))           // frame stage
            .Build();

        var steps = RecipeJsonSerializer.Serialize(recipe)["steps"]!.AsArray();
        var ops = steps.Select(s => s!["op"]!.GetValue<string>()).ToList();
        // After canonical sort: autoOrient (implicit) is NOT serialized (we only render explicit steps),
        // then frame -> resize -> encode by stage order
        ops.Should().Equal("sample", "resize", "encodeAs");
    }

    [Fact]
    public void Serializes_mutators_as_lowercase_string_array()
    {
        var recipe = MediaRecipe.New()
            .Mutators(MutatorKind.Dimensions | MutatorKind.Format)
            .Build();
        var mutators = RecipeJsonSerializer.Serialize(recipe)["mutators"]!.AsArray();
        var values = mutators.Select(m => m!.GetValue<string>()).ToList();
        values.Should().Contain("dimensions");
        values.Should().Contain("format");
    }

    [Fact]
    public void SerializeAll_includes_top_level_metadata()
    {
        var recipes = new[] { MediaRecipe.New().WithName("a").EncodeAs("webp").Build() };
        var shortcuts = new[] { "png", "jpeg", "webp", "gif" };
        var json = RecipeJsonSerializer.SerializeAll(recipes, shortcuts);
        json["recipes"]!.AsArray().Should().HaveCount(1);
        json["formatShortcuts"]!.AsArray().Should().HaveCount(4);
        json["paramAliases"]!.AsObject().Should().ContainKey("w");
        json["adHocSteps"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public void SerializeAsAppSettings_wraps_recipe_for_paste()
    {
        var recipe = MediaRecipe.New()
            .WithName("poster")
            .EncodeAs("webp", 80)
            .Build();
        var json = RecipeJsonSerializer.SerializeAsAppSettings(recipe);
        json["Koan"]!["Media"]!["Recipes"]!["poster"].Should().NotBeNull();
        // 'name' / 'fingerprint' / 'source' are stripped from the appsettings form
        var inner = json["Koan"]!["Media"]!["Recipes"]!["poster"]!.AsObject();
        inner.ContainsKey("name").Should().BeFalse();
        inner.ContainsKey("fingerprint").Should().BeFalse();
        inner.ContainsKey("steps").Should().BeTrue();
        inner.ContainsKey("mutators").Should().BeTrue();
    }

    [Fact]
    public void Round_trip_via_appsettings_form_preserves_steps()
    {
        var original = MediaRecipe.New()
            .WithName("trip")
            .Resize(800, 600).Name("size").Primary()
            .EncodeAs("webp", 80)
            .Mutators(MutatorKind.Common)
            .Build();

        var appsettings = RecipeJsonSerializer.SerializeAsAppSettings(original);
        var inner = appsettings["Koan"]!["Media"]!["Recipes"]!["trip"]!.AsObject();
        // Materialize the inner JSON as a ConfiguredRecipe
        var configured = System.Text.Json.JsonSerializer.Deserialize<ConfiguredRecipe>(
            inner.ToJsonString(),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        configured.Should().NotBeNull();

        var rebuilt = ConfiguredRecipeBinder.Bind("trip", configured!, RecipeSource.Config);
        rebuilt.Fingerprint().Should().Be(original.Fingerprint(),
            "JSON round-trip via appsettings must produce an identical recipe");
    }

    [Fact]
    public void Serializes_strip_kinds_as_canonical_csv()
    {
        var recipe = MediaRecipe.New()
            .Strip(MetadataKinds.Exif | MetadataKinds.Xmp)
            .EncodeAs("webp")
            .Build();
        var steps = RecipeJsonSerializer.Serialize(recipe)["steps"]!.AsArray();
        var strip = steps.Single(s => s!["op"]!.GetValue<string>() == "strip");
        strip!["kinds"]!.GetValue<string>().Should().Be("exif,xmp");
    }

    [Fact]
    public void Serializes_strip_all_as_all_keyword()
    {
        var recipe = MediaRecipe.New()
            .Strip(MetadataKinds.All)
            .EncodeAs("webp")
            .Build();
        var strip = RecipeJsonSerializer.Serialize(recipe)["steps"]!.AsArray()
            .Single(s => s!["op"]!.GetValue<string>() == "strip");
        strip!["kinds"]!.GetValue<string>().Should().Be("all");
    }
}
